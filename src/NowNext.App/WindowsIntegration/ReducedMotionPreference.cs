using Windows.UI.ViewManagement;

namespace NowNext.App.WindowsIntegration;

public interface IReducedMotionPreference
{
    public bool IsReducedMotionEnabled { get; }
}

public sealed class WindowsReducedMotionPreference : IReducedMotionPreference
{
    private readonly UISettings _uiSettings = new();

    public bool IsReducedMotionEnabled => !_uiSettings.AnimationsEnabled;
}
