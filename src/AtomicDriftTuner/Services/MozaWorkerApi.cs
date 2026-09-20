using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AtomicDriftTuner.Services;

public sealed class MozaWorkerFailureException(string message, Exception inner) : IOException(message, inner);

// Keep vendor native code outside the desktop process. A native access violation,
// SDK abort or shutdown hang cannot be recovered by a catch in the WPF process.
public sealed class MozaWorkerApi : IMozaMotorApi
{
    public const string WorkerArgument = "--moza-sdk-worker";
    public sealed record Request(string Operation, string? Text = null, int Value = 0);
    public sealed record Response(bool Success, string Text = "", int Value = 0);
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _process;
    private readonly TimeSpan _timeout;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private bool _disposed;

    public MozaWorkerApi(string folder) : this(folder, StartInfo, TimeSpan.FromSeconds(20)) { }

    // Tests supply a separate fixture process; never a vendor DLL or real device.
    public MozaWorkerApi(string folder, Func<string, ProcessStartInfo> startInfo, TimeSpan timeout)
    {
        _timeout = timeout;
        var name = "adt-moza-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _process = new Process { StartInfo = startInfo(name) };
        try
        {
            if (!_process.Start()) throw new IOException("The SDK helper could not start.");
            using var deadline = new CancellationTokenSource(_timeout);
            var connected = _pipe.WaitForConnectionAsync(deadline.Token);
            var exited = _process.WaitForExitAsync(deadline.Token);
            var first = Task.WhenAny(connected, exited).GetAwaiter().GetResult();
            if (first != connected) throw new IOException("The SDK helper exited before connecting.");
            connected.GetAwaiter().GetResult();
            _reader = new StreamReader(_pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 1024, leaveOpen: true);
            Exchange(new("open", folder));
        }
        catch (Exception ex)
        {
            Dispose();
            throw new MozaWorkerFailureException(FailureMessage(ex), ex);
        }
    }

    private static ProcessStartInfo StartInfo(string pipe)
    {
        // Location is empty in the self-contained single-file release.
        var assembly = typeof(MozaWorkerApi).Assembly.Location;
        var executable = string.IsNullOrEmpty(assembly) ? Environment.ProcessPath!
            : Path.ChangeExtension(assembly, ".exe");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory };
        info.ArgumentList.Add(WorkerArgument);
        info.ArgumentList.Add(pipe);
        return info;
    }

    private static string FailureMessage(Exception ex) =>
        "The MOZA SDK connection failed or stopped responding. ADT is still running. " +
        "Check that Pit House is running, the base is connected, and the x64 SDK files match an SDK-compatible Pit House version. " +
        "Do not retry Apply automatically; if this happened during Apply, some settings may have changed. " +
        "Details: " + ex.Message;

    private Response Exchange(Request request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using var deadline = new CancellationTokenSource(_timeout);
            _writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token).GetAwaiter().GetResult();
            _writer.FlushAsync(deadline.Token).GetAwaiter().GetResult();
            var line = _reader.ReadLineAsync(deadline.Token).AsTask().GetAwaiter().GetResult();
            if (line is null || line.Length > 16_384) throw new IOException("The SDK helper closed unexpectedly or returned an invalid response.");
            var response = JsonSerializer.Deserialize<Response>(line) ?? throw new IOException("Missing SDK response.");
            if (!response.Success) throw new InvalidOperationException(response.Text);
            return response;
        }
        catch (InvalidOperationException) { throw; } // Ordinary SDK errors keep their useful message.
        catch (Exception ex)
        {
            var detail = request.Operation + ": " + ex.Message;
            try { if (_process.HasExited) detail += $" (helper exit 0x{_process.ExitCode:X8})"; }
            catch (InvalidOperationException) { }
            Dispose(); // Never reconnect/retry a request whose outcome is unknown.
            throw new MozaWorkerFailureException(FailureMessage(new IOException(detail)), ex);
        }
    }

    public string DeviceName() => Exchange(new("device")).Text;
    public int Read(string key) { PitHouseCatalog.Find(key); return Exchange(new("read", key)).Value; }
    public void Write(string key, int value)
    {
        if (!PitHouseCatalog.Find(key).Accepts(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Exchange(new("write", key, value));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Closing the pipe asks the worker to dispose on its SDK thread. A broken
        // native teardown is confined to that process and must not hide an Apply backup.
        _pipe.Dispose();
        try { if (!_process.WaitForExit(1500)) { _process.Kill(entireProcessTree: true); _process.WaitForExit(1500); } }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { _process.Dispose(); }
    }

    public static int RunWorker(string pipeName, Func<string, IMozaMotorApi>? factory = null)
    {
        // No desktop window, recorder, updater or crash dialog in this mode.
        // The random current-user-only pipe is the only transport; nothing is exposed on LAN.
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        pipe.Connect(10_000);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        IMozaMotorApi? api = null;
        try
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Length > 16_384) return 2;
                Response response;
                try
                {
                    var request = JsonSerializer.Deserialize<Request>(line) ?? throw new InvalidDataException("Missing request.");
                    if (request.Operation == "open" && api is null)
                    {
                        api = (factory ?? (folder => new MozaNativeApi(folder)))(request.Text ?? "");
                        response = new(true);
                    }
                    else
                    {
                        if (api is null) throw new InvalidOperationException("SDK not opened.");
                        response = request.Operation switch
                        {
                            "device" => new(true, api.DeviceName()),
                            "read" => new(true, Value: api.Read(request.Text ?? "")),
                            "write" => Write(api, request),
                            _ => throw new InvalidDataException("Unknown SDK request.")
                        };
                    }
                }
                catch (Exception ex) { response = new(false, ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message); }
                writer.WriteLine(JsonSerializer.Serialize(response));
            }
        }
        catch (IOException) { return 0; } // The parent closed its pipe; never reconnect.
        finally
        {
            try { api?.Dispose(); }
            finally { try { writer.Dispose(); } catch (IOException) { } }
        }
        return 0;
    }

    private static Response Write(IMozaMotorApi api, Request request)
    {
        var key = request.Text ?? "";
        if (!PitHouseCatalog.Find(key).Accepts(request.Value)) throw new InvalidDataException("Invalid SDK value.");
        api.Write(key, request.Value);
        return new(true);
    }
}
