using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AtomicDriftTuner.Services;

public interface IMozaMotorApi : IDisposable
{
    string DeviceName();
    int Read(string key);
    void Write(string key, int value);
}

/// <summary>
/// Optional native adapter. ABI verified from the C# wrapper's P/Invoke metadata:
/// Cdecl; int getMotorX_C(ref int error); int setMotorX_C(int value).
/// No MOZA library is loaded until the user explicitly reads or applies settings.
/// </summary>
public sealed class MozaNativeApi : IMozaMotorApi
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Lifecycle();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Device(int type, ref int error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Getter(ref int error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Setter(int value);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    private IntPtr _library;
    private bool _initialized;
    private readonly Dictionary<string, Getter> _getters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Setter> _setters = new(StringComparer.Ordinal);
    private readonly Lifecycle _remove;
    private readonly Device _device;

    public MozaNativeApi(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Choose the MOZA SDK_CSharp/x64 folder in Setup & Paths (x86 for 32-bit ADT). Manual entry does not need the SDK.");
        var root = Path.GetFullPath(folder);
        foreach (var name in new[] { "MOZA_API_C.dll", "MOZA_SDK.dll" })
            if (!File.Exists(Path.Combine(root, name))) throw new FileNotFoundException($"{name} was not found. Select the SDK_CSharp/{(Environment.Is64BitProcess ? "x64" : "x86")} folder from MOZA's SDK.");
        // Resolve dependencies beside the chosen DLL and in Windows' default safe
        // directories; never change PATH or the process-wide DLL search directory.
        _library = LoadLibraryExW(Path.Combine(root, "MOZA_API_C.dll"), IntPtr.Zero, 0x100 | 0x1000);
        if (_library == IntPtr.Zero) throw new InvalidOperationException("Could not load the MOZA SDK. Check matching x64/x86 files, SDK-compatible Pit House and Microsoft's Visual C++ runtime. " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        try
        {
            _remove = Export<Lifecycle>("removeMozaSDK_C");
            _device = Export<Device>("getDeviceParent_C");
            foreach (var setting in PitHouseCatalog.Settings)
            {
                _getters.Add(setting.Key, Export<Getter>("getMotor" + setting.Key + "_C"));
                _setters.Add(setting.Key, Export<Setter>("setMotor" + setting.Key + "_C"));
            }
            Export<Lifecycle>("installMozaSDK_C")();
            _initialized = true;
            // MOZA's supplied sdk_api_test.cc waits three seconds after loading
            // before reading motor settings. This constructor runs off the UI
            // thread inside PitHouseService, with the integration gate held.
            Thread.Sleep(3000);
        }
        catch { NativeLibrary.Free(_library); _library = IntPtr.Zero; throw; }
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public string DeviceName()
    {
        ObjectDisposedException.ThrowIf(_library == IntPtr.Zero, this);
        int error = 0;
        var pointer = _device(0, ref error); // PRODUCT_WHEELBASE
        Check(error);
        var name = pointer == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pointer) ?? "";
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("No MOZA wheelbase was detected. Power/connect the base, start SDK-compatible Pit House, then Read again.");
        return name;
    }
    public int Read(string key)
    {
        ObjectDisposedException.ThrowIf(_library == IntPtr.Zero, this);
        PitHouseCatalog.Find(key);
        int error = 0;
        int value = _getters[key](ref error);
        Check(error);
        return value;
    }
    public void Write(string key, int value)
    {
        ObjectDisposedException.ThrowIf(_library == IntPtr.Zero, this);
        if (!PitHouseCatalog.Find(key).Accepts(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Check(_setters[key](value));
    }
    public static void Check(int code)
    {
        if (code == 0) return;
        var message = code switch {
            1 => "MOZA SDK support is not installed. Install SDK-compatible Pit House.",
            2 => "No MOZA device detected. Connect and power your wheelbase.",
            3 => "Pit House rejected a setting outside its supported range.",
            4 => "Pit House rejected a parameter; check SDK/firmware compatibility.",
            9 => "Wheelbase firmware is too old for this SDK. Check MOZA's supported firmware.",
            10 => "Pit House is not ready. Start SDK-compatible Pit House, wait for the base, then Read again.",
            _ => "The MOZA SDK reported an error. Check Pit House and the connected base." };
        throw new InvalidOperationException($"{message} (SDK code {code})");
    }
    public void Dispose()
    {
        if (_library == IntPtr.Zero) return;
        try { if (_initialized) _remove(); }
        finally { _initialized = false; NativeLibrary.Free(_library); _library = IntPtr.Zero; }
    }
}
