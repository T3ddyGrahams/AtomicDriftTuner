using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

/// <summary>An immutable numeric proposal. Only the in-game companion can apply it after a driver click.</summary>
public sealed class PitSetupPlan
{
    internal PitSetupPlan(string carId, string label, IEnumerable<PitSetupChange> changes,
        IDictionary<string, double> baselineValues)
    {
        PlanId = Guid.NewGuid().ToString("N");
        CarId = carId;
        Label = label;
        Changes = Array.AsReadOnly(changes.ToArray());
        BaselineValues = new ReadOnlyDictionary<string, double>(
            new SortedDictionary<string, double>(baselineValues, StringComparer.Ordinal));
    }

    [JsonPropertyName("protocolVersion")] public int ProtocolVersion => 1;
    [JsonPropertyName("planId")] public string PlanId { get; }
    [JsonPropertyName("carId")] public string CarId { get; }
    [JsonPropertyName("label")] public string Label { get; }
    [JsonPropertyName("changes")] public IReadOnlyList<PitSetupChange> Changes { get; }
    [JsonPropertyName("baselineValues")]
    [JsonConverter(typeof(PitSetupBaselineJsonConverter))]
    public IReadOnlyDictionary<string, double> BaselineValues { get; }
}

/// <summary>INI section identifiers retain their spelling even when API dictionary keys use camel case.</summary>
internal sealed class PitSetupBaselineJsonConverter : JsonConverter<IReadOnlyDictionary<string, double>>
{
    public PitSetupBaselineJsonConverter() { }

    public override IReadOnlyDictionary<string, double> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Pit setup baselines are created from validated desktop analyses.");

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, double> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value) writer.WriteNumber(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}

public sealed class PitSetupChange
{
    internal PitSetupChange(string section, double before, double after)
    {
        Section = section;
        Before = before;
        After = after;
    }

    [JsonPropertyName("section")] public string Section { get; }
    [JsonPropertyName("before")] public double Before { get; }
    [JsonPropertyName("after")] public double After { get; }
}
