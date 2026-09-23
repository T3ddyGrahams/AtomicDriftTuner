using System.Buffers.Binary;
using System.IO;
using System.Text;
using AtomicDriftTuner.Services;

internal static class PackedArchiveChecks
{
    // Fixed externally calculated vectors keep the encoder independent of production key generation.
    private static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bdc_streetspec_s13_v4"] = "9-124-85-220-198-143-24-53",
        ["ks_audi_sport_quattro"] = "230-190-101-8-154-33-64-112",
        ["ks_nissan_gtr"] = "117-109-203-121-54-234-64-115",
        ["physics_car"] = "152-156-177-91-150-61-59-115",
        ["a"] = "97-0-0-131-66-101-171-171"
    };

    internal static void WriteArchive(string carFolderPath, IReadOnlyDictionary<string, string> entries)
    {
        Directory.CreateDirectory(carFolderPath);
        File.WriteAllBytes(Path.Combine(carFolderPath, "data.acd"), Encode(entries.Select(p => (p.Key, Encoding.UTF8.GetBytes(p.Value))), Path.GetFileName(carFolderPath)));
    }

    internal static byte[] Encode(IEnumerable<(string Name, byte[] Data)> entries, string carId = "physics_car", bool header = false)
    {
        var key = Encoding.ASCII.GetBytes(Keys[carId]);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        if (header) { writer.Write(-1111); writer.Write(1); }
        foreach (var entry in entries)
        {
            var name = Encoding.UTF8.GetBytes(entry.Name);
            writer.Write(name.Length); writer.Write(name); writer.Write(entry.Data.Length);
            for (var i = 0; i < entry.Data.Length; i++) writer.Write((int)unchecked((byte)(entry.Data[i] + key[i % key.Length])));
        }
        return stream.ToArray();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refuse(byte[] archive, string id = "physics_car")
    {
        try { PackedCarDataReader.Read(archive, id); }
        catch (InvalidDataException) { return; }
        throw new Exception("Malformed or unsupported packed archive was accepted.");
    }
    private static byte[] One(string name = "car.ini", string content = "[BASIC]\nTOTALMASS=1200\n") => Encode([(name, Encoding.UTF8.GetBytes(content))]);

    public static void Run(Action<string, Action> test, string root)
    {
        test("packed reader decodes a frozen archive without the test encoder", () =>
        {
            var archive = Convert.FromHexString("05000000782E696E6908000000940000008F0000008A0000003A000000830000006D0000005E0000003B000000");
            Check(Encoding.UTF8.GetString(PackedCarDataReader.Read(archive, "a")["x.ini"]) == "[X]\nV=1\n", "Frozen ordinary archive decoded incorrectly.");
        });
        test("packed reader decodes frozen key vectors and resets the key for each entry", () =>
        {
            foreach (var car in Keys.Keys)
            {
                const string text = "[BASIC]\nTOTALMASS=1200\n; Unicode author: José\n";
                var archive = Encode([("CAR.INI", Encoding.UTF8.GetBytes(text)), ("engine.ini", Encoding.UTF8.GetBytes(text))], car);
                var before = archive.ToArray();
                var decoded = PackedCarDataReader.Read(archive, car.ToUpperInvariant());
                Check(decoded.Count == 2 && Encoding.UTF8.GetString(decoded["car.ini"]) == text
                    && decoded["car.ini"].SequenceEqual(decoded["ENGINE.INI"]), "Known key or per-entry reset failed.");
                Check(archive.SequenceEqual(before), "Reader modified the input archive.");
            }
        });
        test("packed reader handles the optional legacy header and supported text extensions", () =>
        {
            var archive = Encode([("car.ini", Encoding.UTF8.GetBytes("[BASIC]\nX=1")), ("power.lut", Encoding.UTF8.GetBytes("1000|100\n")),
                ("final.rto", Encoding.UTF8.GetBytes("4.30|4.3\n")), ("script.lua", Encoding.UTF8.GetBytes("-- inspected only\n"))], header: true);
            Check(PackedCarDataReader.Read(archive, "physics_car").Count == 4, "Supported files or optional header lost.");
        });
        test("packed reader skips unrelated binary content without interpreting it", () =>
        {
            var archive = Encode([("car.ini", Encoding.UTF8.GetBytes("[BASIC]\nX=1")), ("thumbnail.bin", new byte[] { 0, 255, 128, 7 })]);
            var decoded = PackedCarDataReader.Read(archive, "physics_car");
            Check(decoded.Count == 1 && decoded.ContainsKey("car.ini"), "Unrelated binary was decoded or blocked supported physics.");
        });
        test("packed reader rejects unsafe and non-ASCII names even for ignored files", () =>
        {
            foreach (var name in new[] { "../car.ini", "..\\car.bin", "/car.ini", "C:car.ini", "car.ini:stream", "CON.ini", "LPT1.bin", "car.ini.", " car.ini", "car\0.ini", "cár.ini", "", "a*.bin" })
                Refuse(One(name));
        });
        test("packed reader rejects case-insensitive duplicate selected and ignored names", () =>
        {
            foreach (var name in new[] { "car.ini", "extra.bin" })
                Refuse(Encode([(name, Array.Empty<byte>()), (name.ToUpperInvariant(), Array.Empty<byte>())]));
        });
        test("packed reader refuses unsupported car identifiers", () =>
        {
            foreach (var id in new[] { "", "../physics_car", "cár", "physics_car ", "CON", new string('x', 256) }) Refuse(One(), id);
        });
        test("packed reader rejects every truncated prefix without producing partial physics", () =>
        {
            foreach (var withHeader in new[] { false, true })
            {
                var archive = Encode([("car.ini", Encoding.UTF8.GetBytes("[BASIC]\nX=1"))], header: withHeader);
                for (var n = 0; n < archive.Length; n++) Refuse(archive[..n]);
            }
        });
        test("packed reader rejects negative and oversized names and payload lengths", () =>
        {
            foreach (var bad in new[] { -1, 0, 256, int.MaxValue })
            {
                var archive = One(); BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(0, 4), bad); Refuse(archive);
            }
            foreach (var bad in new[] { -1, PackedCarDataReader.MaxEntryBytes + 1, int.MaxValue })
            {
                var archive = One(); BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(11, 4), bad); Refuse(archive);
            }
        });
        test("packed reader rejects nonordinary padding and trailing partial records", () =>
        {
            var archive = One(); archive[16] = 1; Refuse(archive);
            Refuse([.. One(), 1]);
            Refuse([.. One(), 255, 255, 255, 255]);
        });
        test("packed reader refuses binary and unsupported text encoding in physics files", () =>
        {
            foreach (var data in new[] { new byte[] { 0, 1, 2 }, new byte[] { 255, 254, 91, 0 }, new byte[] { 0xC3, 0x28 }, Encoding.UTF8.GetBytes("[BASIC]\nX=\u0085") })
                Refuse(Encode([("car.ini", data)]));
        });
        test("packed reader enforces archive and entry-count resource limits", () =>
        {
            Refuse(new byte[PackedCarDataReader.MaxArchiveBytes + 1]);
            var max = Enumerable.Range(0, PackedCarDataReader.MaxEntries).Select(i => ($"file_{i}.ini", Array.Empty<byte>())).ToArray();
            Check(PackedCarDataReader.Read(Encode(max), "physics_car").Count == PackedCarDataReader.MaxEntries, "Valid count boundary rejected.");
            Refuse(Encode(max.Append(("overflow.ini", Array.Empty<byte>()))));
        });
        test("packed reader permits a bounded file and rejects a larger ignored file", () =>
        {
            var valid = Enumerable.Repeat((byte)' ', PackedCarDataReader.MaxEntryBytes).ToArray();
            Check(PackedCarDataReader.Read(Encode([("car.ini", valid)]), "physics_car")["car.ini"].Length == valid.Length, "Valid file boundary rejected.");
            Refuse(Encode([("extra.bin", new byte[PackedCarDataReader.MaxEntryBytes + 1])]));
        });
    }
}
