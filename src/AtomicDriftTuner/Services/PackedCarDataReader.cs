using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Bounded, in-memory reader for ordinary AC data.acd framing. It never executes content,
/// guesses keys, follows archive paths or writes to the installed car. Format references:
/// docs/PACKED_FORMAT_SOURCES.md. Additional protection and non-text variants are unsupported.
/// </summary>
public static class PackedCarDataReader
{
    public const int MaxArchiveBytes = 64 * 1024 * 1024;
    public const int MaxEntryBytes = 1024 * 1024;
    public const int MaxDecodedBytes = 16 * 1024 * 1024;
    public const int MaxEntries = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ini", ".lut", ".rto", ".lua" };

    public static IReadOnlyDictionary<string, byte[]> Read(byte[] archive, string carId)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Length is < 4 or > MaxArchiveBytes)
            throw Invalid("The packed car data is empty, truncated or larger than ADT's reading limit.");
        if (!SafeName(carId))
            throw Invalid("The car folder name is unsupported. ADT reads ordinary packed data with an ASCII car identifier.");

        var key = DeriveKey(carId.ToLowerInvariant());
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        if (ReadInt(archive, ref position) == -1111)
        {
            // The optional legacy marker has one opaque 32-bit field; it does not authenticate content.
            _ = ReadInt(archive, ref position);
        }
        else position = 0;

        long decodedTotal = 0;
        var count = 0;
        while (position < archive.Length)
        {
            if (++count > MaxEntries) throw Invalid("The packed car data contains too many files for ADT to read.");
            var nameLength = ReadInt(archive, ref position);
            if (nameLength is < 1 or > 255 || nameLength > archive.Length - position)
                throw Invalid("The packed car data has an invalid file name length.");
            var nameBytes = archive.AsSpan(position, nameLength);
            foreach (var value in nameBytes)
                if (value is < 32 or > 126) throw Invalid("The packed car data contains an unsupported file name.");
            var name = Encoding.ASCII.GetString(nameBytes);
            position += nameLength;
            if (!SafeName(name) || !seen.Add(name))
                throw Invalid("The packed car data contains an unsafe or duplicate file name.");

            var length = ReadInt(archive, ref position);
            if (length is < 0 or > MaxEntryBytes)
                throw Invalid("A file inside the packed car data exceeds ADT's reading limit or has an invalid length.");
            decodedTotal += length;
            if (decodedTotal > MaxDecodedBytes)
                throw Invalid("The packed car data exceeds ADT's decoded-data limit.");
            var storedLength = (long)length * 4;
            if (storedLength > archive.Length - position)
                throw Invalid("The packed car data ended before a complete file could be read.");

            var supported = SupportedExtensions.Contains(Path.GetExtension(name));
            var decoded = supported ? new byte[length] : null;
            for (var index = 0; index < length; index++)
            {
                var word = position + index * 4;
                // Ordinary records use zero padding. Do not silently interpret another/protected variant.
                if (archive[word + 1] != 0 || archive[word + 2] != 0 || archive[word + 3] != 0)
                    throw Invalid("The packed car data uses unsupported or corrupt record padding.");
                if (decoded is not null) decoded[index] = unchecked((byte)(archive[word] - key[index % key.Length]));
            }
            position += (int)storedLength;
            if (decoded is null) continue;
            ValidateText(decoded);
            result.Add(name, decoded);
        }
        if (count == 0) throw Invalid("The packed car data contains no complete file records.");
        return new ReadOnlyDictionary<string, byte[]>(result);
    }

    private static int ReadInt(byte[] bytes, ref int position)
    {
        if (bytes.Length - position < 4) throw Invalid("The packed car data has a truncated record or header.");
        var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
        position += 4;
        return value;
    }

    private static bool SafeName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255 || name != name.Trim() || name is "." or ".."
            || name.EndsWith('.') || name.Any(c => c is < ' ' or > '~' || "<>:\"/\\|?*".Contains(c))) return false;
        var stem = name.Split('.')[0];
        return !stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            && !stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            && !stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            && !stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }

    private static void ValidateText(byte[] bytes)
    {
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException)
        { throw Invalid("A packed physics file is not supported UTF-8 text. It may use another encoding, protection or a mismatched car folder name."); }
        if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
            throw Invalid("A packed physics file contains binary or protected data that ADT cannot interpret.");
    }

    private static byte[] DeriveKey(string id)
    {
        // Independently implemented format arithmetic. Integer division truncates toward zero;
        // unchecked arithmetic retains the ordinary format's 32-bit wrap behavior.
        var parts = new int[] { 0, 0, 0, 5763, 66, 101, 171, 171 };
        unchecked
        {
            foreach (var c in id) parts[0] += c;
            for (var i = 0; i + 1 < id.Length; i += 2) parts[1] = parts[1] * id[i] - id[i + 1];
            for (var i = 1; i < id.Length - 3; i += 3)
                parts[2] = parts[2] * id[i] / (id[i + 1] + 27) - 27 - id[i - 1];
            for (var i = 1; i < id.Length; i++) parts[3] -= id[i];
            for (var i = 1; i < id.Length - 4; i += 4)
                parts[4] = parts[4] * (id[i] + 15) * (id[i - 1] + 15) + 22;
            for (var i = 0; i < id.Length - 2; i += 2) { parts[5] -= id[i]; parts[6] %= id[i]; }
            for (var i = 0; i + 1 < id.Length; i++) parts[7] = parts[7] / id[i] + id[i + 1];
        }
        return Encoding.ASCII.GetBytes(string.Join("-", parts.Select(p => (p & 255).ToString(CultureInfo.InvariantCulture))));
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
