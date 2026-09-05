using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Reads Assetto Corsa's static shared-memory page to identify the car and
/// track loaded in the current session.
///
/// This reader is intentionally read-only and independent from the high-rate
/// physics telemetry stream.
/// </summary>
public sealed class AssettoCorsaSessionIdentityReader
{
    private const string StaticMapName =
        @"Local\acpmf_static";

    private const int MaxIdentityLength =
        128;

    private const int MaxRaceIniBytes =
        1024 * 1024;

    public AssettoCorsaSessionIdentity? TryRead()
    {
        try
        {
            using var map =
                MemoryMappedFile.OpenExisting(
                    StaticMapName,
                    MemoryMappedFileRights.Read);

            var info =
                ReadStruct<AcStaticHeader>(
                    map);

            var car =
                NormalizeIdentityValue(
                    info.CarModel);

            // Some AC/CSP/Content Manager session combinations expose a
            // numeric slot value, commonly "0", in the static shared-memory
            // carModel field instead of the installed car-folder ID.
            //
            // In that case, use the launcher-generated race.ini as a
            // read-only fallback. A valid shared-memory model ID always
            // remains authoritative.
            if (!LooksLikeCarModelId(car))
            {
                var raceIniCar =
                    TryReadRaceIniCarModel();

                if (!string.IsNullOrWhiteSpace(
                        raceIniCar))
                {
                    car =
                        raceIniCar;
                }
            }

            if (!LooksLikeCarModelId(
                    car))
            {
                return null;
            }

            return new AssettoCorsaSessionIdentity
            {
                CarModel =
                    car,

                Track =
                    NormalizeIdentityValue(
                        info.Track),

                SharedMemoryVersion =
                    NormalizeIdentityValue(
                        info.SmVersion),

                AssettoCorsaVersion =
                    NormalizeIdentityValue(
                        info.AcVersion)
            };
        }
        catch (
            Exception ex)
            when (
                ex is FileNotFoundException ||
                ex is UnauthorizedAccessException ||
                ex is IOException ||
                ex is ArgumentException ||
                ex is MarshalDirectiveException)
        {
            // Identity detection is advisory. An unavailable or malformed
            // static page must never interfere with telemetry, tuning, or
            // ADT Remote.
            return null;
        }
    }

    private static T ReadStruct<T>(
        MemoryMappedFile map)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(
            map);

        var size =
            Marshal.SizeOf<T>();

        using var stream =
            map.CreateViewStream(
                0,
                size,
                MemoryMappedFileAccess.Read);

        var bytes =
            new byte[size];

        var offset =
            0;

        while (offset < bytes.Length)
        {
            var read =
                stream.Read(
                    bytes,
                    offset,
                    bytes.Length - offset);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Assetto Corsa static shared memory returned an incomplete frame.");
            }

            offset +=
                read;
        }

        var handle =
            GCHandle.Alloc(
                bytes,
                GCHandleType.Pinned);

        try
        {
            return Marshal.PtrToStructure<T>(
                handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static string NormalizeIdentityValue(
        string? value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        var normalized =
            value
                .TrimEnd('\0')
                .Trim();

        if (normalized.Length > MaxIdentityLength)
        {
            normalized =
                normalized[..MaxIdentityLength];
        }

        var filtered =
            new string(
                normalized
                    .Where(
                        character =>
                            !char.IsControl(character))
                    .ToArray());

        return filtered.Trim();
    }

    private static bool LooksLikeCarModelId(
        string? value)
    {
        var model =
            NormalizeIdentityValue(
                value);

        if (
            string.IsNullOrWhiteSpace(model) ||
            model == "-")
        {
            return false;
        }

        if (
            model.Contains('/') ||
            model.Contains('\\') ||
            model.Contains(':'))
        {
            return false;
        }

        // AC car-folder IDs normally contain at least one letter.
        // A bare numeric value is generally a car slot rather than an
        // installed-car folder identifier.
        return model.Any(
            char.IsLetter);
    }

    private static string? TryReadRaceIniCarModel()
    {
        try
        {
            var documents =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);

            if (string.IsNullOrWhiteSpace(
                    documents))
            {
                return null;
            }

            var path =
                Path.Combine(
                    documents,
                    "Assetto Corsa",
                    "cfg",
                    "race.ini");

            if (!File.Exists(
                    path))
            {
                return null;
            }

            var info =
                new FileInfo(
                    path);

            if (
                info.Length <= 0 ||
                info.Length > MaxRaceIniBytes)
            {
                return null;
            }

            var section =
                string.Empty;

            string? raceSectionModel =
                null;

            foreach (var rawLine in File.ReadLines(
                         path))
            {
                var line =
                    rawLine.Trim();

                if (
                    line.Length == 0 ||
                    line.StartsWith(';') ||
                    line.StartsWith('#'))
                {
                    continue;
                }

                if (
                    line.StartsWith('[') &&
                    line.EndsWith(']'))
                {
                    section =
                        line[1..^1]
                            .Trim();

                    continue;
                }

                var equals =
                    line.IndexOf('=');

                if (equals <= 0)
                {
                    continue;
                }

                var key =
                    line[..equals]
                        .Trim();

                if (!key.Equals(
                        "MODEL",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value =
                    NormalizeIdentityValue(
                        line[(equals + 1)..]
                            .Trim()
                            .Trim('"'));

                if (!LooksLikeCarModelId(
                        value))
                {
                    continue;
                }

                if (section.Equals(
                        "CAR_0",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }

                if (section.Equals(
                        "RACE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    raceSectionModel =
                        value;
                }
            }

            return raceSectionModel;
        }
        catch (
            Exception ex)
            when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException)
        {
            // The fallback is advisory. If race.ini is unavailable, ADT
            // simply continues without automatic session identity.
            return null;
        }
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 4,
        CharSet = CharSet.Unicode)]
    private struct AcStaticHeader
    {
        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 15)]
        public string SmVersion;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 15)]
        public string AcVersion;

        public int NumberOfSessions;
        public int NumCars;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 33)]
        public string CarModel;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 33)]
        public string Track;
    }
}

public sealed class AssettoCorsaSessionIdentity
{
    public string CarModel { get; init; } =
        string.Empty;

    public string Track { get; init; } =
        string.Empty;

    public string SharedMemoryVersion { get; init; } =
        string.Empty;

    public string AssettoCorsaVersion { get; init; } =
        string.Empty;
}
