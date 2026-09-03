using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Reads Assetto Corsa's static shared-memory page to identify the car/track
/// loaded in the current session. This reader is intentionally read-only and
/// independent from the high-rate physics telemetry stream.
/// </summary>
public sealed class AssettoCorsaSessionIdentityReader
{
    private const string StaticMapName = @"Local\acpmf_static";

    public AssettoCorsaSessionIdentity? TryRead()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(StaticMapName);
            using var stream = map.CreateViewStream(
                0,
                Marshal.SizeOf<AcStaticHeader>(),
                MemoryMappedFileAccess.Read);

            byte[] bytes = new byte[Marshal.SizeOf<AcStaticHeader>()];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                    return null;
                offset += read;
            }

            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var info = Marshal.PtrToStructure<AcStaticHeader>(handle.AddrOfPinnedObject());
                string car = Clean(info.CarModel);
                if (string.IsNullOrWhiteSpace(car))
                    return null;

                return new AssettoCorsaSessionIdentity
                {
                    CarModel = car,
                    Track = Clean(info.Track),
                    SharedMemoryVersion = Clean(info.SmVersion),
                    AssettoCorsaVersion = Clean(info.AcVersion)
                };
            }
            finally
            {
                handle.Free();
            }
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch
        {
            // Auto detection is advisory. A malformed/unavailable static page
            // must never interfere with tuning, telemetry, or the remote server.
            return null;
        }
    }

    private static string Clean(string? value) =>
        (value ?? string.Empty).Trim().TrimEnd('\0');

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    private struct AcStaticHeader
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
        public string SmVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
        public string AcVersion;

        public int NumberOfSessions;
        public int NumCars;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
        public string CarModel;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
        public string Track;
    }
}

public sealed class AssettoCorsaSessionIdentity
{
    public string CarModel { get; init; } = "";
    public string Track { get; init; } = "";
    public string SharedMemoryVersion { get; init; } = "";
    public string AssettoCorsaVersion { get; init; } = "";
}
