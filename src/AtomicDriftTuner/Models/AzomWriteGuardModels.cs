namespace AtomicDriftTuner.Models;

public sealed class AzomInteractiveWriteResult
{
    public string PropertyName { get; set; } = "";
    public bool WasWritten { get; set; }
    public bool WasSuperseded { get; set; }
    public string Method { get; set; } = "";
}
