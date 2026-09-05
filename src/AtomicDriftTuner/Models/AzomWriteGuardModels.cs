namespace AtomicDriftTuner.Models;

public sealed class AzomInteractiveWriteResult
{
    private string _propertyName =
        string.Empty;

    private string _method =
        string.Empty;

    public string PropertyName
    {
        get => _propertyName;

        set =>
            _propertyName =
                value?.Trim() ??
                string.Empty;
    }

    public bool WasWritten { get; set; }

    public bool WasSuperseded { get; set; }

    public string Method
    {
        get => _method;

        set =>
            _method =
                value?.Trim() ??
                string.Empty;
    }
}