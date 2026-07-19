using Windows.Storage;

namespace NowNext.App.WindowsIntegration;

public interface IWindowsUserSettings
{
    public bool KeepDisplayAwakeDuringSessions { get; set; }

    public bool StartFullScreen { get; set; }

    public void Clear();
}

public sealed class WindowsUserSettings : IWindowsUserSettings
{
    private const string KeepDisplayAwakeKey = "KeepDisplayAwakeDuringSessions";
    private const string StartFullScreenKey = "StartFullScreen";
    private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

    public bool KeepDisplayAwakeDuringSessions
    {
        get => ReadBoolean(KeepDisplayAwakeKey);
        set => _settings.Values[KeepDisplayAwakeKey] = value;
    }

    public bool StartFullScreen
    {
        get => ReadBoolean(StartFullScreenKey);
        set => _settings.Values[StartFullScreenKey] = value;
    }

    public void Clear()
    {
        _settings.Values.Remove(KeepDisplayAwakeKey);
        _settings.Values.Remove(StartFullScreenKey);
    }

    private bool ReadBoolean(string key)
    {
        return _settings.Values.TryGetValue(key, out object? value)
            && value is bool enabled
            && enabled;
    }
}
