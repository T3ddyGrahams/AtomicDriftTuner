using System.IO;
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

                // Some AC/CSP/Content Manager session combinations expose a numeric
                // slot value (commonly "0") in the static shared-memory carModel
                // field instead of the installed car folder id. In that case, use
                // the launcher-generated race.ini as a read-only fallback.
                //
                // race.ini is written for the session that AC is currently running
                // and [CAR_0] MODEL maps to content\cars\<folder>. We only use the
                // fallback when shared memory is clearly not a usable model id so a
                // valid shared-memory value always remains authoritative.
                if (!LooksLikeCarModelId(car))
                {
                    string? raceIniCar = TryReadRaceIniCarModel();
                    if (!string.IsNullOrWhiteSpace(raceIniCar))
                        car = raceIniCar;
                }

                if (!LooksLikeCarModelId(car))
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

    private static bool LooksLikeCarModelId(string? value)
    {
        string model = Clean(value);
        if (string.IsNullOrWhiteSpace(model) || model == "-")
            return false;

        // AC car folder ids contain letters. A bare numeric value is a car slot,
        // not an installed-car folder name.
        return model.Any(char.IsLetter);
    }

    private static string? TryReadRaceIniCarModel()
    {
        try
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
                return null;

            string path = Path.Combine(documents, "Assetto Corsa", "cfg", "race.ini");
            if (!File.Exists(path))
                return null;

            string section = string.Empty;
            string? raceSectionModel = null;

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line[..equals].Trim();
                if (!key.Equals("MODEL", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line[(equals + 1)..].Trim().Trim('\"');
                if (!LooksLikeCarModelId(value))
                    continue;

                if (section.Equals("CAR_0", StringComparison.OrdinalIgnoreCase))
                    return value;

                if (section.Equals("RACE", StringComparison.OrdinalIgnoreCase))
                    raceSectionModel = value;
            }

            return raceSectionModel;
        }
        catch
        {
            // Identity detection is advisory; an inaccessible race.ini must never
            // interfere with the rest of ADT.
            return null;
        }
    }

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
