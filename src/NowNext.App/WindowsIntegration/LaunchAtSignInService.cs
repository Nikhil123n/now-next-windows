using Windows.ApplicationModel;

namespace NowNext.App.WindowsIntegration;

public enum LaunchAtSignInState
{
    Disabled,
    Enabled,
    DisabledByUser,
    DisabledByPolicy,
    EnabledByPolicy,
}

public interface ILaunchAtSignInService
{
    public System.Threading.Tasks.Task<LaunchAtSignInState> GetStateAsync();

    public System.Threading.Tasks.Task<LaunchAtSignInState> SetEnabledAsync(bool enabled);
}

public sealed class WindowsLaunchAtSignInService : ILaunchAtSignInService
{
    internal const string StartupTaskId = "NowNextStartupTask";

    public async System.Threading.Tasks.Task<LaunchAtSignInState> GetStateAsync()
    {
        StartupTask startupTask = await StartupTask.GetAsync(StartupTaskId);
        return MapState(startupTask.State);
    }

    public async System.Threading.Tasks.Task<LaunchAtSignInState> SetEnabledAsync(bool enabled)
    {
        StartupTask startupTask = await StartupTask.GetAsync(StartupTaskId);
        if (enabled)
        {
            StartupTaskState state = await startupTask.RequestEnableAsync();
            return MapState(state);
        }

        if (startupTask.State == StartupTaskState.Enabled)
        {
            startupTask.Disable();
        }

        return MapState(startupTask.State);
    }

    private static LaunchAtSignInState MapState(StartupTaskState state)
    {
        return state switch
        {
            StartupTaskState.Disabled => LaunchAtSignInState.Disabled,
            StartupTaskState.Enabled => LaunchAtSignInState.Enabled,
            StartupTaskState.DisabledByUser => LaunchAtSignInState.DisabledByUser,
            StartupTaskState.DisabledByPolicy => LaunchAtSignInState.DisabledByPolicy,
            StartupTaskState.EnabledByPolicy => LaunchAtSignInState.EnabledByPolicy,
            _ => throw new InvalidOperationException("Windows returned an unknown startup state."),
        };
    }
}
