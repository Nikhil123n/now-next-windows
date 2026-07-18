using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace NowNext.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(string statusMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        InitializeComponent();
        StatusText.Text = statusMessage;
        AutomationProperties.SetName(StatusText, statusMessage);
    }

    internal void SetStatus(string statusMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        StatusText.Text = statusMessage;
        AutomationProperties.SetName(StatusText, statusMessage);
    }
}
