using Microsoft.UI.Xaml;
using NowNext.App.Persistence;

namespace NowNext.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        const string readyMessage = "NOW/NEXT prototype is ready.";
        const string storageFailureMessage = "NOW/NEXT could not initialize local storage.";
        string statusMessage;

        try
        {
            using TodayPlanStore store = TodayPlanStore.CreateForCurrentUser();
            await store.InitializeAsync();
            statusMessage = readyMessage;
        }
        catch (TodayPlanStorageException)
        {
            statusMessage = storageFailureMessage;
        }

        _window = new MainWindow(statusMessage);
        _window.Activate();
    }
}
