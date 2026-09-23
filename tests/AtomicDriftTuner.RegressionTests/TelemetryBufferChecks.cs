using System.IO.MemoryMappedFiles;
using System.Reflection;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class TelemetryBufferChecks
{
    internal static void Run(Action<string, Action> test)
    {
        test("background capture retains every fresh frame and ignores duplicate polls", () =>
        {
            using var fixture = new Fixture();
            var capture = fixture.Hub.StartRecordingCapture();
            for (var i = 2; i <= 100; i++) { fixture.Read(i); fixture.Read(i); }
            var batch = capture.Drain();
            Check(!batch.Overflowed && batch.Samples.Select(s => s.PacketId).SequenceEqual(Enumerable.Range(2, 99)),
                "capture lost, duplicated, reordered or included a pre-start sample");
            Check(capture.Drain().Samples.Count == 0, "drain returned frames twice");
            fixture.Read(101);
            Check(fixture.Hub.StopRecordingCapture(capture).Samples.Single().PacketId == 101, "stop lost the final pending frame");
            fixture.Read(102);
            Check(capture.Drain().Samples.Count == 0, "stopped capture kept recording");
        });
        test("recording evidence is isolated from live consumers and other recordings", () =>
        {
            using var fixture = new Fixture();
            var first = fixture.Hub.StartRecordingCapture();
            var second = fixture.Hub.StartRecordingCapture();
            fixture.Read(2);
            fixture.Hub.GetSnapshot().Sample!.TimeSeconds = 987;
            var one = first.Drain().Samples.Single();
            Check(one.TimeSeconds != 987, "live snapshot mutation changed pending evidence");
            one.PacketId = 100;
            Check(second.Drain().Samples.Single().PacketId == 2, "one recorder mutated another recorder's evidence");
            fixture.Hub.StopRecordingCapture(first);
            fixture.Read(3);
            Check(first.Drain().Samples.Count == 0 && second.Drain().Samples.Single().PacketId == 3, "stop detached the wrong capture");
            var third = fixture.Hub.StartRecordingCapture();
            fixture.Read(4);
            Check(third.Drain().Samples.Single().PacketId == 4, "new run inherited old buffered frames");
        });
        test("frozen and failed telemetry never fabricates buffered frames", () =>
        {
            using var fixture = new Fixture();
            var capture = fixture.Hub.StartRecordingCapture();
            fixture.Read(2);
            Set(fixture.Hub, "_updatedUtc", DateTimeOffset.UtcNow.AddSeconds(-1));
            fixture.Read(2); // Production stale-packet path releases the anonymous map.
            var batch = fixture.Hub.StopRecordingCapture(capture);
            Check(batch.Samples.Count == 1 && batch.Samples[0].PacketId == 2, "stale/failure path duplicated or discarded the last good frame");
        });
        test("buffer overflow is bounded explicit and preserves the original prefix", () =>
        {
            using var fixture = new Fixture();
            var capture = fixture.Hub.StartRecordingCapture();
            for (var i = 2; i <= 3201; i++) fixture.Read(i);
            var batch = capture.Drain();
            Check(batch.Overflowed && batch.Samples.Count == 3000 && batch.Samples[0].PacketId == 2 && batch.Samples[^1].PacketId == 3001,
                "overflow silently overwrote evidence or grew without a bound");
            fixture.Read(3202);
            Check(capture.Drain() is { Overflowed: true, Samples.Count: 0 }, "overflow resumed silently after losing evidence");
            fixture.Hub.StopRecordingCapture(capture);
            var next = fixture.Hub.StartRecordingCapture();
            fixture.Read(3203);
            Check(next.Drain() is { Overflowed: false, Samples.Count: 1 }, "overflow poisoned a new recording");
        });
    }

    private sealed class Fixture : IDisposable
    {
        public readonly TelemetryHubService Hub = new();
        private readonly MemoryMappedFile _map = MemoryMappedFile.CreateNew(null, 4096);
        public Fixture() { Set(Get(Hub, "_reader")!, "_physicsMap", _map); Read(1); }
        public void Read(int packet)
        {
            using (var view = _map.CreateViewAccessor()) view.Write(0, packet);
            typeof(TelemetryHubService).GetMethod("ReadOnceLocked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Hub, null);
        }
        public void Dispose() { Hub.Dispose(); _map.Dispose(); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
