using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Optional spatial side-channel. A nonce proves that CSP acquired the pose after
/// our request. The elapsed request/response interval plus CSP physics-state age
/// bounds alignment uncertainty; it is never represented as an exact physics tick.
/// Missing/late/torn/replayed responses cannot affect native telemetry capture.
/// </summary>
public sealed class TrackPositionReader : IDisposable
{
    public const string RequestMap = @"Local\ADT.TrackPosition.Request.v1";
    public const string ResponseMap = @"Local\ADT.TrackPosition.Position.v1";
    public const int ResponseSize = 488;
    private MemoryMappedFile? _request, _response;
    private MemoryMappedViewAccessor? _requestView, _responseView;
    private int _token = Random.Shared.Next(1, 1_000_000_000);
    private double? _sent;
    private double? _sourceTime;
    private double _retryAt;

    public TrackPosition? Read(double now)
    {
        try
        {
            if (_requestView is null)
            {
                _request = MemoryMappedFile.CreateOrOpen(RequestMap, 8);
                _requestView = _request.CreateViewAccessor();
            }
            if (_responseView is null && now >= _retryAt)
            {
                _retryAt = now + 1;
                _response = MemoryMappedFile.OpenExisting(ResponseMap, MemoryMappedFileRights.Read);
                _responseView = _response.CreateViewAccessor(0, ResponseSize, MemoryMappedFileAccess.Read);
            }
            TrackPosition? result = null;
            bool consumed = false;
            if (_responseView is not null && _sent is double sent)
            {
                int first = _responseView.ReadInt32(0);
                var bytes = new byte[ResponseSize];
                _responseView.ReadArray(0, bytes, 0, bytes.Length);
                Thread.MemoryBarrier();
                int last = _responseView.ReadInt32(0);
                if (first == last && first > 0 && (first & 1) == 0 && BitConverter.ToInt32(bytes, 8) == _token)
                {
                    consumed = true;
                    result = Decode(bytes, _token, now - sent, _sourceTime);
                    // A reset must discard its first response, then allow a fresh timeline.
                    var source = BitConverter.ToDouble(bytes, 16);
                    if (double.IsFinite(source)) _sourceTime = source;
                }
            }
            if (_sent is null || consumed || now - _sent > .08 || now < _sent)
            {
                _token = _token >= int.MaxValue - 1 ? 1 : _token + 1;
                _sent = now;
                _requestView.Write(0, _token);
                _requestView.Flush();
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Optional data is absent. Keep core recorder reliability unchanged.
            return null;
        }
    }

    public static TrackPosition? Decode(byte[] bytes, int token, double roundTrip, double? previousSourceTime)
    {
        if (bytes.Length != ResponseSize || BitConverter.ToInt32(bytes, 4) != 1 ||
            BitConverter.ToInt32(bytes, 8) != token || BitConverter.ToInt32(bytes, 12) != 1 ||
            !double.IsFinite(roundTrip) || roundTrip is < 0 or > .08) return null;
        double D(int offset) => BitConverter.ToDouble(bytes, offset);
        string S(int offset)
        {
            int end = Array.IndexOf(bytes, (byte)0, offset, 128);
            if (end < offset) return "";
            var text = Encoding.ASCII.GetString(bytes, offset, end - offset);
            return text.Length <= 100 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? text : "";
        }
        double age = D(24), stamp = D(16);
        if (!double.IsFinite(age) || age is < 0 or > .05 || roundTrip + age > .1 ||
            !double.IsFinite(stamp) || stamp < 0 || previousSourceTime is double previous && stamp <= previous) return null;
        string track = S(104), layout = S(232), car = S(360);
        // Distinguish a captured empty default layout from malformed/truncated identity.
        if (track.Length == 0 || car.Length == 0 || layout.Length == 0 && bytes[232] != 0) return null;
        if (new[] { D(32), D(40), D(48) }.Any(x => !double.IsFinite(x) || Math.Abs(x) > 1_000_000)) return null;
        bool spline = D(56) is >= 0 and <= 1 && new[] { D(64), D(72), D(80) }.All(x => double.IsFinite(x) && Math.Abs(x) < 1_000_000);
        // Bounds are mod-authored context. Do not infer them from a driven route.
        bool boundaries = spline && D(88) is > .1 and < 100 && D(96) is > .1 and < 100;
        return new TrackPosition { Track = track, Layout = layout, Car = car, SourceTimeMs = stamp,
            AlignmentUncertaintySeconds = roundTrip + age, X = D(32), Y = D(40), Z = D(48),
            SplineProgress = spline ? D(56) : null, SplineX = spline ? D(64) : null,
            SplineY = spline ? D(72) : null, SplineZ = spline ? D(80) : null,
            LeftBoundaryM = boundaries ? D(88) : null, RightBoundaryM = boundaries ? D(96) : null };
    }

    public void Dispose()
    {
        _requestView?.Dispose(); _responseView?.Dispose(); _request?.Dispose(); _response?.Dispose();
        _requestView = _responseView = null; _request = _response = null;
    }
}
