using System.Collections.ObjectModel;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed record PitHouseReading(string Device, DateTimeOffset CapturedUtc,
    IReadOnlyDictionary<string, int> Values, IReadOnlyDictionary<string, string> Errors);
public sealed record PitHouseChange(string Key, int Before, int Target);
public sealed record PitHousePlan(string Device, DateTimeOffset CapturedUtc, IReadOnlyList<PitHouseChange> Changes);
public sealed class PitHouseBackup
{
    public string Schema { get; set; } = "adt/pithouse-backup/1";
    public string Device { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public List<PitHouseChange> Changes { get; set; } = [];
}
public sealed record PitHouseApplyResult(bool Verified, string Message, string BackupPath);

public sealed class PitHouseService
{
    private readonly Func<IMozaMotorApi> _open;
    private readonly Action _requireProvider;
    private readonly string _backups;
    public PitHouseService(string folder) : this(() => new MozaWorkerApi(folder),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner", "PitHouseBackups"),
        () => FfbProviderOptions.Require(FfbProvider.MozaPitHouse)) { }
    // The fake API used in tests never loads native code or accesses a device.
    public PitHouseService(Func<IMozaMotorApi> open, string backups, Action requireProvider)
    { _open = open; _backups = backups; _requireProvider = requireProvider; }

    public async Task<PitHouseReading> ReadAsync()
    {
        using var gate = await AzomLiveController.AcquireLiveWriteGateAsync(CancellationToken.None);
        return await Task.Run(() => { _requireProvider(); using var api = _open(); return Read(api); });
    }
    private static PitHouseReading Read(IMozaMotorApi api)
    {
        var device = api.DeviceName();
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in PitHouseCatalog.Settings)
        {
            try
            {
                var value = api.Read(setting.Key);
                if (!setting.Accepts(value)) throw new InvalidDataException("Readback outside the documented SDK range; this control is unavailable.");
                values.Add(setting.Key, value);
            }
            catch (Exception ex) when (ex is not MozaWorkerFailureException) { errors.Add(setting.Key, ex.Message); }
        }
        if (api.DeviceName() != device) throw new InvalidOperationException("The connected base changed during the read. Read again.");
        return new(device, DateTimeOffset.UtcNow, new ReadOnlyDictionary<string, int>(values), new ReadOnlyDictionary<string, string>(errors));
    }
    public static PitHousePlan Plan(PitHouseReading reading, IEnumerable<KeyValuePair<string, int>> targets)
    {
        var changes = new List<PitHouseChange>();
        foreach (var target in targets)
        {
            var setting = PitHouseCatalog.Find(target.Key);
            if (!setting.Accepts(target.Value)) throw new InvalidDataException($"{setting.Label}: target {target.Value} is outside the SDK range {setting.Min}–{setting.Max}. It was not clamped or sent.");
            if (!reading.Values.TryGetValue(target.Key, out var before) || !setting.Accepts(before)) throw new InvalidDataException($"{setting.Label} is not safely readable.");
            if (before != target.Value) changes.Add(new(target.Key, before, target.Value));
        }
        var plan = new PitHousePlan(reading.Device, reading.CapturedUtc, changes.AsReadOnly());
        Validate(plan);
        return plan;
    }
    private static void Validate(PitHousePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Device) || plan.Changes is null || plan.Changes.Count < 1 || plan.Changes.Count > PitHouseCatalog.Settings.Count ||
            plan.Changes.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != plan.Changes.Count)
            throw new InvalidDataException("Select at least one changed, supported setting. Duplicate controls are not allowed.");
        foreach (var c in plan.Changes)
        {
            var setting = PitHouseCatalog.Find(c.Key);
            if (!setting.Accepts(c.Before) || !setting.Accepts(c.Target)) throw new InvalidDataException("A Pit House value is outside its documented range.");
        }
    }
    public async Task<PitHouseApplyResult> ApplyAsync(PitHousePlan reviewed)
    {
        // Freeze caller-owned collections before waiting for another integration.
        var plan = reviewed with { Changes = Array.AsReadOnly(reviewed.Changes.ToArray()) };
        Validate(plan);
        using var gate = await AzomLiveController.AcquireLiveWriteGateAsync(CancellationToken.None);
        return await Task.Run(() => Apply(plan));
    }
    private PitHouseApplyResult Apply(PitHousePlan plan)
    {
        _requireProvider();
        var age = DateTimeOffset.UtcNow - plan.CapturedUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(2)) throw new InvalidOperationException("The preview expired. Read Pit House and review the settings again.");
        using var api = _open();
        EnsureDevice(api, plan.Device);
        foreach (var c in plan.Changes)
            if (api.Read(c.Key) != c.Before) throw new InvalidOperationException("Pit House values changed after your preview. Nothing was sent. Read again before applying.");
        _requireProvider();
        var backup = new PitHouseBackup { Device = plan.Device, CreatedUtc = DateTimeOffset.UtcNow, Changes = plan.Changes.ToList() };
        Directory.CreateDirectory(_backups);
        var path = Path.Combine(_backups, $"pithouse-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        // A durable recovery copy must exist before the first physical write.
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { JsonSerializer.Serialize(stream, backup, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
        try
        {
            foreach (var c in plan.Changes)
            {
                _requireProvider();
                EnsureDevice(api, plan.Device);
                if (api.Read(c.Key) != c.Before) throw new InvalidOperationException("A value changed during the batch; remaining writes were stopped.");
                api.Write(c.Key, c.Target);
                Thread.Sleep(350);
                if (api.Read(c.Key) != c.Target) throw new InvalidOperationException("Pit House did not confirm " + PitHouseCatalog.Find(c.Key).Label + ".");
            }
            EnsureDevice(api, plan.Device);
            foreach (var c in plan.Changes)
                if (api.Read(c.Key) != c.Target) throw new InvalidOperationException("Final readback changed. The batch is not verified.");
            return new(true, $"Verified {plan.Changes.Count} selected setting(s) by SDK readback. Check the base in Pit House before driving. Other controls were left untouched.", path);
        }
        catch (Exception ex)
        {
            // A setter can act even when its response/readback fails. Never retry
            // automatically, claim rollback or discard the original values.
            return new(false, "Stopped: " + ex.Message + " Some settings may have changed. Read again and review the backup to restore selected original values. No automatic retry or rollback was attempted.", path);
        }
    }
    private static void EnsureDevice(IMozaMotorApi api, string expected)
    {
        if (!string.Equals(api.DeviceName(), expected, StringComparison.Ordinal)) throw new InvalidOperationException("The detected base differs from the reviewed base. Read again.");
    }
    public static PitHouseBackup LoadBackup(string path)
    {
        if (new FileInfo(path).Length is <= 0 or > 100_000) throw new InvalidDataException("Invalid Pit House backup size.");
        var backup = JsonSerializer.Deserialize<PitHouseBackup>(File.ReadAllText(path)) ?? throw new InvalidDataException("Unreadable backup.");
        if (backup.Schema != "adt/pithouse-backup/1") throw new InvalidDataException("Unsupported backup format.");
        Validate(new(backup.Device, backup.CreatedUtc, backup.Changes));
        return backup;
    }
    public static IReadOnlyDictionary<string, int> RestoreTargets(PitHouseReading current, PitHouseBackup backup)
    {
        if (!string.Equals(current.Device, backup.Device, StringComparison.Ordinal)) throw new InvalidOperationException("This backup reports a different base. Restore it manually in Pit House.");
        Validate(new(backup.Device, backup.CreatedUtc, backup.Changes));
        return new ReadOnlyDictionary<string, int>(backup.Changes.ToDictionary(x => x.Key, x => x.Before, StringComparer.Ordinal));
    }
}
