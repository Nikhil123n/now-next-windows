using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NowNext.App.Persistence;
using NowNext.App.Presentation;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.App;

public sealed partial class MainWindow : Window
{
    private const int ControlsInactivitySeconds = 6;
    private const int CheckpointIntervalTicks = 20;

    private readonly TodayPlanStore? _store;
    private readonly FocusSessionRuntime? _sessionRuntime;
    private readonly DispatcherQueueTimer _focusProjectionTimer;
    private readonly DispatcherQueueTimer _colonTimer;
    private readonly DispatcherQueueTimer _controlsInactivityTimer;
    private readonly UISettings _uiSettings = new();
    private AppWindow? _appWindow;
    private DomainTask? _editingTask;
    private DomainTask? _focusedTask;
    private bool _commandInProgress;
    private bool _colonVisible = true;
    private bool _allowClose;
    private bool _closeCheckpointInProgress;
    private int _ticksSinceCheckpoint;

    public MainWindow(TodayPlanStore store, FocusSessionRuntime sessionRuntime)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sessionRuntime = sessionRuntime ?? throw new ArgumentNullException(nameof(sessionRuntime));
        InitializeComponent();
        _focusProjectionTimer = DispatcherQueue.CreateTimer();
        _focusProjectionTimer.Interval = TimeSpan.FromMilliseconds(250);
        _focusProjectionTimer.Tick += OnFocusProjectionTimerTick;
        _colonTimer = DispatcherQueue.CreateTimer();
        _colonTimer.Interval = TimeSpan.FromSeconds(1);
        _colonTimer.Tick += OnColonTimerTick;
        _controlsInactivityTimer = DispatcherQueue.CreateTimer();
        _controlsInactivityTimer.Interval = TimeSpan.FromSeconds(ControlsInactivitySeconds);
        _controlsInactivityTimer.IsRepeating = false;
        _controlsInactivityTimer.Tick += OnControlsInactivityTimerTick;
    }

    public MainWindow(string statusMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        InitializeComponent();
        _focusProjectionTimer = DispatcherQueue.CreateTimer();
        _colonTimer = DispatcherQueue.CreateTimer();
        _controlsInactivityTimer = DispatcherQueue.CreateTimer();
        AddTaskButton.IsEnabled = false;
        SetStatus(statusMessage);
    }

    public ObservableCollection<TodayTaskItem> TodayItems { get; } = [];

    internal void SetStatus(string statusMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        StatusText.Text = statusMessage;
        AutomationProperties.SetName(StatusText, statusMessage);

        if (FocusScreen.Visibility == Visibility.Visible)
        {
            FocusControlError.Text = statusMessage;
            RevealControls();
        }
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        ConfigureAppWindow();
        TodayDateText.Text = TimeProvider.System.GetLocalNow().ToString(
            "D",
            CultureInfo.CurrentCulture);

        if (_store is null || _sessionRuntime is null)
        {
            return;
        }

        try
        {
            await ReloadTodayAsync();
            FocusSession? current = _sessionRuntime.Current;
            if (current is not null && !IsTerminal(current.State))
            {
                _focusedTask = TodayItems
                    .Select(item => item.Task)
                    .SingleOrDefault(task => task.Id == current.TaskId);
                if (_focusedTask is null)
                {
                    SetStatus("The saved focus session does not match today's plan.");
                    return;
                }

                if (current.State is ReadySessionState)
                {
                    await _sessionRuntime.ExecuteAsync(new StartSession());
                }

                ShowFocusScreen();
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("NOW/NEXT could not restore today's saved state.");
        }
    }

    private void ConfigureAppWindow()
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += OnAppWindowClosing;
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || _closeCheckpointInProgress || _sessionRuntime is null)
        {
            return;
        }

        FocusSession? current = _sessionRuntime.Current;
        if (current?.State is not (
            FocusingSessionState
            or OvertimeSessionState
            or LandingSessionState
            or BreakSessionState))
        {
            return;
        }

        args.Cancel = true;
        _closeCheckpointInProgress = true;
        try
        {
            await _sessionRuntime.InterruptForSuspensionAsync();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("NOW/NEXT could not save the final recovery checkpoint.");
        }
        finally
        {
            _allowClose = true;
            _closeCheckpointInProgress = false;
            Close();
        }
    }

    private async void OnAddTaskClick(object sender, RoutedEventArgs args)
    {
        _editingTask = null;
        TaskEditorDialog.Title = "Add task";
        TaskEditorDialog.PrimaryButtonText = "Add task";
        TaskEditorError.Text = string.Empty;
        FullTitleInput.Text = string.Empty;
        FocusLabelInput.Text = string.Empty;
        DefinitionOfDoneInput.Text = string.Empty;
        FirstActionInput.Text = string.Empty;
        PlannedStartInput.SelectedTime = TimeProvider.System.GetLocalNow().TimeOfDay;
        DurationInput.Value = 25;
        TimingModeInput.SelectedIndex = 0;
        ScheduleTypeInput.SelectedIndex = 1;
        ImportanceInput.SelectedIndex = 0;
        await ShowTaskEditorAsync();
    }

    private async void OnEditTaskClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetItem(sender, out TodayTaskItem item))
        {
            return;
        }

        _editingTask = item.Task;
        TaskEditorDialog.Title = "Edit task";
        TaskEditorDialog.PrimaryButtonText = "Save task";
        TaskEditorError.Text = string.Empty;
        FullTitleInput.Text = item.Task.FullTitle;
        FocusLabelInput.Text = item.Task.ShortFocusLabel;
        DefinitionOfDoneInput.Text = item.Task.DefinitionOfDone;
        FirstActionInput.Text = item.Task.FirstPhysicalAction;
        PlannedStartInput.SelectedTime = item.Task.PlannedStart.ToTimeSpan();
        DurationInput.Value = item.Task.PlannedDuration.TotalMinutes;
        TimingModeInput.SelectedIndex = item.Task.TimingMode == TimingMode.CountUp ? 0 : 1;
        ScheduleTypeInput.SelectedIndex = item.Task.ScheduleType == ScheduleType.Fixed ? 0 : 1;
        ImportanceInput.SelectedIndex = item.Task.Importance == TaskImportance.Normal ? 0 : 1;
        await ShowTaskEditorAsync();
    }

    private async System.Threading.Tasks.Task ShowTaskEditorAsync()
    {
        TaskEditorDialog.XamlRoot = RootGrid.XamlRoot;
        TaskEditorDialog.Visibility = Visibility.Visible;
        try
        {
            await TaskEditorDialog.ShowAsync();
        }
        finally
        {
            TaskEditorDialog.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnTaskEditorPrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            if (_store is null)
            {
                args.Cancel = true;
                return;
            }

            var input = new TaskEditorInput(
                FullTitleInput.Text,
                FocusLabelInput.Text,
                DefinitionOfDoneInput.Text,
                FirstActionInput.Text,
                PlannedStartInput.SelectedTime,
                DurationInput.Value,
                TimingModeInput.SelectedIndex == 0 ? TimingMode.CountUp : TimingMode.Countdown,
                ScheduleTypeInput.SelectedIndex == 0
                    ? ScheduleType.Fixed
                    : ScheduleType.Flexible,
                ImportanceInput.SelectedIndex == 0
                    ? TaskImportance.Normal
                    : TaskImportance.Important);
            TaskId taskId = _editingTask?.Id ?? new TaskId(Guid.NewGuid());
            string? nextAction = _editingTask?.NextPhysicalAction;
            TaskState state = _editingTask?.State ?? TaskState.Planned;
            if (!input.TryCreateTask(taskId, nextAction, state, out DomainTask? task, out string error))
            {
                TaskEditorError.Text = error;
                args.Cancel = true;
                return;
            }

            if (_editingTask is null)
            {
                await _store.CreateTaskAsync(task!);
            }
            else
            {
                await _store.EditTaskAsync(task!);
            }

            await ReloadTodayAsync();
            StatusText.Text = _editingTask is null ? "Task added." : "Task updated.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            TaskEditorError.Text = "The task could not be saved. Check the fields and try again.";
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnDeleteTaskClick(object sender, RoutedEventArgs args)
    {
        if (_store is null || !TryGetItem(sender, out TodayTaskItem item))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete this task from today?",
            Content = "Its row is retained for session-history integrity, but it will leave today's plan.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _store.DeleteTaskAsync(item.Task.Id);
            await ReloadTodayAsync();
            StatusText.Text = "Task removed from today.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("The task could not be deleted while it has unresolved focus work.");
        }
    }

    private async void OnMoveEarlierClick(object sender, RoutedEventArgs args)
    {
        await MoveTaskAsync(sender, -1);
    }

    private async void OnMoveLaterClick(object sender, RoutedEventArgs args)
    {
        await MoveTaskAsync(sender, 1);
    }

    private async System.Threading.Tasks.Task MoveTaskAsync(object sender, int offset)
    {
        if (_store is null || !TryGetItem(sender, out TodayTaskItem item))
        {
            return;
        }

        int currentIndex = TodayItems.IndexOf(item);
        int targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= TodayItems.Count)
        {
            return;
        }

        List<TaskId> order = TodayItems.Select(value => value.Task.Id).ToList();
        (order[currentIndex], order[targetIndex]) = (order[targetIndex], order[currentIndex]);
        try
        {
            await _store.ReorderTasksAsync(order);
            await ReloadTodayAsync();
            TodayTaskList.ScrollIntoView(TodayItems[targetIndex]);
            StatusText.Text = "Task order updated.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("The task order could not be updated.");
        }
    }

    private async void OnStartTaskClick(object sender, RoutedEventArgs args)
    {
        if (_sessionRuntime is null || !TryGetItem(sender, out TodayTaskItem item))
        {
            return;
        }

        try
        {
            FocusSession? current = _sessionRuntime.Current;
            if (current?.State is ReadySessionState && current.TaskId == item.Task.Id)
            {
                await _sessionRuntime.ExecuteAsync(new StartSession());
            }
            else
            {
                await _sessionRuntime.CreateAsync(
                    new SessionId(Guid.NewGuid()),
                    item.Task.Id,
                    item.Task.TimingMode,
                    item.Task.PlannedDuration);
                await _sessionRuntime.ExecuteAsync(new StartSession());
            }

            _focusedTask = item.Task;
            ShowFocusScreen();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("Resolve the current focus session before starting another task.");
        }
    }

    private void ShowFocusScreen()
    {
        if (_sessionRuntime?.Current is null || _focusedTask is null)
        {
            return;
        }

        TodayScreen.Visibility = Visibility.Collapsed;
        FocusScreen.Visibility = Visibility.Visible;
        FocusLabelText.Text = _focusedTask.ShortFocusLabel;
        FocusControls.Visibility = Visibility.Collapsed;
        FocusControlError.Text = string.Empty;
        _colonVisible = true;
        TimerFirstColonText.Opacity = 1;
        TimerSecondColonText.Opacity = 1;
        _ticksSinceCheckpoint = 0;
        UpdateFocusProjection();
        _focusProjectionTimer.Start();
        _colonTimer.Start();
        _appWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
        _ = FocusScreen.Focus(FocusState.Programmatic);
    }

    private async System.Threading.Tasks.Task ShowTodayScreenAsync()
    {
        _focusProjectionTimer.Stop();
        _colonTimer.Stop();
        _controlsInactivityTimer.Stop();
        FocusControls.Visibility = Visibility.Collapsed;
        RecoveryPanel.Visibility = Visibility.Collapsed;
        FocusScreen.Visibility = Visibility.Collapsed;
        TodayScreen.Visibility = Visibility.Visible;
        _appWindow?.SetPresenter(AppWindowPresenterKind.Default);
        await ReloadTodayAsync();
        _ = TodayTaskList.Focus(FocusState.Programmatic);
    }

    private async void OnFocusProjectionTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_sessionRuntime?.Current is null || _commandInProgress)
        {
            return;
        }

        try
        {
            SessionView projected = _sessionRuntime.GetCurrentView();
            UpdateFocusProjection(projected);
            FocusSession? current = _sessionRuntime.Current;
            if (current is null)
            {
                return;
            }
            bool running = current.State is
                FocusingSessionState
                or OvertimeSessionState
                or LandingSessionState;
            bool crossedBoundary = projected.State != current.State.Kind;
            if (running && (crossedBoundary || ++_ticksSinceCheckpoint >= CheckpointIntervalTicks))
            {
                _ticksSinceCheckpoint = 0;
                await ExecuteCommandAsync(new RefreshSession(), returnToTodayOnOutcome: false);
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            FocusControlError.Text = "The focus checkpoint could not be saved.";
            RevealControls();
        }
    }

    private void UpdateFocusProjection()
    {
        if (_sessionRuntime?.Current is null)
        {
            return;
        }

        UpdateFocusProjection(_sessionRuntime.GetCurrentView());
    }

    private void UpdateFocusProjection(SessionView view)
    {
        TimerDisplayParts display = TimerDisplayFormatter.Format(view);
        TimerPrefixText.Text = display.Prefix;
        TimerHoursText.Text = display.Hours;
        TimerHoursText.Visibility = display.ShowHours ? Visibility.Visible : Visibility.Collapsed;
        TimerFirstColonText.Visibility = display.ShowHours ? Visibility.Visible : Visibility.Collapsed;
        TimerMinutesText.Text = display.Minutes;
        TimerSecondsText.Text = display.Seconds;
        AutomationProperties.SetName(TimerDisplay, display.AccessibleText);

        FocusSession? current = _sessionRuntime?.Current;
        if (current is null)
        {
            return;
        }

        FocusControlAvailability controls = FocusControlPolicy.For(current);
        PauseResumeButton.Content = controls.PauseResumeLabel;
        PauseResumeButton.IsEnabled = controls.CanPauseOrResume;
        FinishButton.IsEnabled = controls.CanFinish;
        ParkButton.IsEnabled = controls.CanPark;
        LandingButton.IsEnabled = controls.CanBeginLanding;
        ContinueButton.IsEnabled = controls.CanContinueOvertime;
        ExtendFiveButton.IsEnabled = controls.CanExtend;
        ExtendTenButton.IsEnabled = controls.CanExtend;
        ExtendFifteenButton.IsEnabled = controls.CanExtend;
        CustomExtendMinutes.IsEnabled = controls.CanExtend;
        CustomExtendButton.IsEnabled = controls.CanExtend;
        RecoveryPanel.Visibility = controls.RequiresRecovery
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (controls.RequiresRecovery)
        {
            FocusControls.Visibility = Visibility.Collapsed;
            _controlsInactivityTimer.Stop();
        }
    }

    private void OnColonTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            _colonVisible = true;
        }
        else
        {
            _colonVisible = !_colonVisible;
        }

        double opacity = _colonVisible ? 1 : 0;
        TimerFirstColonText.Opacity = opacity;
        TimerSecondColonText.Opacity = opacity;
    }

    private void OnFocusPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        RevealControls();
    }

    private void OnFocusPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        RevealControls();
    }

    private void OnFocusTapped(object sender, TappedRoutedEventArgs args)
    {
        RevealControls();
    }

    private async void OnFocusKeyDown(object sender, KeyRoutedEventArgs args)
    {
        RevealControls();
        if (args.Key == VirtualKey.Escape)
        {
            HideControls();
            args.Handled = true;
            return;
        }

        if (args.OriginalSource is TextBox or NumberBox or ButtonBase)
        {
            return;
        }

        switch (args.Key)
        {
            case VirtualKey.Space:
                await PauseOrResumeAsync();
                args.Handled = true;
                break;
            case VirtualKey.F:
                await ExecuteCommandAsync(new CompleteSession());
                args.Handled = true;
                break;
            case VirtualKey.P:
                await ParkAsync();
                args.Handled = true;
                break;
            case VirtualKey.L:
                await ExecuteCommandAsync(new BeginLanding(), returnToTodayOnOutcome: false);
                args.Handled = true;
                break;
            case VirtualKey.O:
                await ExecuteCommandAsync(new ContinueOvertime(), returnToTodayOnOutcome: false);
                args.Handled = true;
                break;
            case VirtualKey.E:
                _ = CustomExtendMinutes.Focus(FocusState.Programmatic);
                args.Handled = true;
                break;
        }
    }

    private void RevealControls()
    {
        FocusSession? current = _sessionRuntime?.Current;
        if (FocusScreen.Visibility != Visibility.Visible
            || current is null
            || current.State is RecoveryRequiredSessionState)
        {
            return;
        }

        FocusControls.Visibility = Visibility.Visible;
        UpdateFocusProjection();
        _controlsInactivityTimer.Stop();
        _controlsInactivityTimer.Start();
    }

    private void OnControlsInactivityTimerTick(DispatcherQueueTimer sender, object args)
    {
        HideControls();
    }

    private void OnHideControlsClick(object sender, RoutedEventArgs args)
    {
        HideControls();
    }

    private void HideControls()
    {
        _controlsInactivityTimer.Stop();
        FocusControls.Visibility = Visibility.Collapsed;
        _ = FocusScreen.Focus(FocusState.Programmatic);
    }

    private async void OnPauseResumeClick(object sender, RoutedEventArgs args)
    {
        await PauseOrResumeAsync();
    }

    private async System.Threading.Tasks.Task PauseOrResumeAsync()
    {
        SessionCommand command = _sessionRuntime?.Current?.State is PausedSessionState
            ? new ResumeSession()
            : new PauseSession();
        await ExecuteCommandAsync(command, returnToTodayOnOutcome: false);
    }

    private async void OnFinishClick(object sender, RoutedEventArgs args)
    {
        await ExecuteCommandAsync(new CompleteSession());
    }

    private async void OnParkClick(object sender, RoutedEventArgs args)
    {
        await ParkAsync();
    }

    private async System.Threading.Tasks.Task ParkAsync()
    {
        var nextActionInput = new TextBox
        {
            Header = "Next physical action",
            Text = _focusedTask?.NextPhysicalAction ?? string.Empty,
        };
        AutomationProperties.SetName(nextActionInput, "Next physical action required to park");
        var dialog = new ContentDialog
        {
            Title = "Park this task",
            Content = nextActionInput,
            PrimaryButtonText = "Park",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nextActionInput.Text))
        {
            FocusControlError.Text = "Parking requires a next physical action.";
            RevealControls();
            return;
        }

        await ExecuteCommandAsync(new ParkSession(nextActionInput.Text));
    }

    private async void OnLandingClick(object sender, RoutedEventArgs args)
    {
        await ExecuteCommandAsync(new BeginLanding(), returnToTodayOnOutcome: false);
    }

    private async void OnContinueClick(object sender, RoutedEventArgs args)
    {
        await ExecuteCommandAsync(new ContinueOvertime(), returnToTodayOnOutcome: false);
    }

    private async void OnExtendFiveClick(object sender, RoutedEventArgs args)
    {
        await ExtendAsync(5);
    }

    private async void OnExtendTenClick(object sender, RoutedEventArgs args)
    {
        await ExtendAsync(10);
    }

    private async void OnExtendFifteenClick(object sender, RoutedEventArgs args)
    {
        await ExtendAsync(15);
    }

    private async void OnCustomExtendClick(object sender, RoutedEventArgs args)
    {
        if (!IsValidTimeSpanMinutes(CustomExtendMinutes.Value))
        {
            FocusControlError.Text = "Enter an extension greater than zero minutes.";
            RevealControls();
            return;
        }

        await ExtendAsync(CustomExtendMinutes.Value);
    }

    private async System.Threading.Tasks.Task ExtendAsync(double minutes)
    {
        await ExecuteCommandAsync(
            new ExtendSession(TimeSpan.FromMinutes(minutes)),
            returnToTodayOnOutcome: false);
    }

    private async void OnResumeWithoutAwayClick(object sender, RoutedEventArgs args)
    {
        await ExecuteCommandAsync(new ResumeWithoutAwayTime(), returnToTodayOnOutcome: false);
    }

    private async void OnResumeIncludingAwayClick(object sender, RoutedEventArgs args)
    {
        if (!double.IsFinite(IncludedAwayMinutes.Value)
            || IncludedAwayMinutes.Value < 0
            || IncludedAwayMinutes.Value >= TimeSpan.MaxValue.TotalMinutes)
        {
            RecoveryError.Text = "Enter zero or a positive number of minutes.";
            return;
        }

        await ExecuteCommandAsync(
            new ResumeIncludingAwayTime(TimeSpan.FromMinutes(IncludedAwayMinutes.Value)),
            returnToTodayOnOutcome: false);
    }

    private async System.Threading.Tasks.Task<bool> ExecuteCommandAsync(
        SessionCommand command,
        bool returnToTodayOnOutcome = true)
    {
        if (_sessionRuntime is null || _commandInProgress)
        {
            return false;
        }

        _commandInProgress = true;
        FocusControlError.Text = string.Empty;
        RecoveryError.Text = string.Empty;
        try
        {
            SessionTransition transition = await _sessionRuntime.ExecuteAsync(command);
            UpdateFocusProjection();
            if (returnToTodayOnOutcome && IsTerminal(transition.Session.State))
            {
                await ShowTodayScreenAsync();
            }
            else if (transition.Session.State is not RecoveryRequiredSessionState
                && command is not RefreshSession)
            {
                RecoveryPanel.Visibility = Visibility.Collapsed;
                RevealControls();
            }

            return true;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            string message = command is ResumeIncludingAwayTime
                ? "The included time exceeds the observed time away. Choose a smaller amount."
                : "That action is not available in the current focus state.";
            if (_sessionRuntime.Current?.State is RecoveryRequiredSessionState)
            {
                RecoveryError.Text = message;
            }
            else
            {
                FocusControlError.Text = message;
                RevealControls();
            }

            return false;
        }
        finally
        {
            _commandInProgress = false;
        }
    }

    private async System.Threading.Tasks.Task ReloadTodayAsync()
    {
        if (_store is null)
        {
            return;
        }

        TodayPlan plan = await _store.LoadTodayPlanAsync();
        TodayItems.Clear();
        foreach (ScheduleEntry entry in plan.Entries)
        {
            TodayItems.Add(new TodayTaskItem(entry));
        }

        EmptyTodayPanel.Visibility = TodayItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        TodayDateText.Text = plan.Date.ToString("D", CultureInfo.CurrentCulture);
    }

    private static bool TryGetItem(object sender, out TodayTaskItem item)
    {
        if ((sender as FrameworkElement)?.DataContext is TodayTaskItem value)
        {
            item = value;
            return true;
        }

        item = null!;
        return false;
    }

    private static bool IsTerminal(SessionState state)
    {
        return state is CompletedSessionState or ParkedSessionState or DayClosedSessionState;
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is
            ArgumentException
            or InvalidOperationException
            or InvalidDataException
            or TodayPlanStorageException;
    }

    private static bool IsValidTimeSpanMinutes(double minutes)
    {
        return double.IsFinite(minutes)
            && minutes > 0
            && minutes < TimeSpan.MaxValue.TotalMinutes;
    }
}
