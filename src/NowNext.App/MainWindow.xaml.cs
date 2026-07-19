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
    private DomainTask? _returningTask;
    private ContextCapsule? _returningContext;
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
        else if (BreakScreen.Visibility == Visibility.Visible)
        {
            BreakError.Text = statusMessage;
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
            if (current is not null && current.State is not (AbandonedSessionState or DayClosedSessionState))
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
                    await ConfirmAndStartTaskAsync(_focusedTask, requireConfirmation: true);
                    return;
                }

                if (current.State is CompletedSessionState or ParkedSessionState)
                {
                    if (current.BreakDuration == TimeSpan.Zero)
                    {
                        await PrepareReturningContextAsync(GetOutcome(current.State));
                        await ShowBreakOfferAsync();
                    }
                }
                else if (IsBreakState(current.State))
                {
                    await PrepareReturningContextAsync(GetOutcome(current.State));
                    ShowBreakScreen();
                }
                else
                {
                    ShowFocusScreen();
                }
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
        if (!TryGetItem(sender, out TodayTaskItem item))
        {
            return;
        }

        await ConfirmAndStartTaskAsync(item.Task, requireConfirmation: false);
    }

    private async System.Threading.Tasks.Task ConfirmAndStartTaskAsync(
        DomainTask task,
        bool requireConfirmation)
    {
        if (_sessionRuntime is null || _store is null)
        {
            return;
        }

        try
        {
            ContextCapsule? capsule = await _store.LoadLatestContextCapsuleAsync(task.Id);
            if (capsule is not null || requireConfirmation)
            {
                var content = new StackPanel { Spacing = 8 };
                content.Children.Add(new TextBlock
                {
                    FontSize = 20,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Text = task.ShortFocusLabel,
                    TextWrapping = TextWrapping.Wrap,
                });
                content.Children.Add(new TextBlock
                {
                    Text = capsule?.NextPhysicalAction ?? task.FirstPhysicalAction,
                    TextWrapping = TextWrapping.Wrap,
                });
                if (capsule?.Note is not null)
                {
                    content.Children.Add(new TextBlock
                    {
                        Opacity = 0.72,
                        Text = capsule.Note,
                        TextWrapping = TextWrapping.Wrap,
                    });
                }

                var dialog = new ContentDialog
                {
                    Title = capsule is null ? "Start this focus session?" : "Saved next action",
                    Content = content,
                    PrimaryButtonText = "Start focus",
                    CloseButtonText = "Not yet",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = RootGrid.XamlRoot,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            FocusSession? current = _sessionRuntime.Current;
            if (current?.State is ReadySessionState && current.TaskId == task.Id)
            {
                await _sessionRuntime.ExecuteAsync(new StartSession());
            }
            else
            {
                await _sessionRuntime.CreateAsync(
                    new SessionId(Guid.NewGuid()),
                    task.Id,
                    task.TimingMode,
                    task.PlannedDuration);
                await _sessionRuntime.ExecuteAsync(new StartSession());
            }

            _focusedTask = task;
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
        BreakScreen.Visibility = Visibility.Collapsed;
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
        BreakRecoveryPanel.Visibility = Visibility.Collapsed;
        BreakScreen.Visibility = Visibility.Collapsed;
        TodayScreen.Visibility = Visibility.Visible;
        _appWindow?.SetPresenter(AppWindowPresenterKind.Default);
        await ReloadTodayAsync();
        _ = TodayTaskList.Focus(FocusState.Programmatic);
    }

    private void ShowBreakScreen()
    {
        if (_sessionRuntime?.Current is null || _focusedTask is null)
        {
            return;
        }

        TodayScreen.Visibility = Visibility.Collapsed;
        FocusScreen.Visibility = Visibility.Collapsed;
        BreakScreen.Visibility = Visibility.Visible;
        FocusControls.Visibility = Visibility.Collapsed;
        RecoveryPanel.Visibility = Visibility.Collapsed;
        BreakError.Text = string.Empty;
        _colonTimer.Stop();
        _ticksSinceCheckpoint = 0;
        UpdateBreakProjection();
        _focusProjectionTimer.Start();
        _appWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
        _ = BreakScreen.Focus(FocusState.Programmatic);
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
            if (BreakScreen.Visibility == Visibility.Visible)
            {
                UpdateBreakProjection(projected);
            }
            else
            {
                UpdateFocusProjection(projected);
            }

            FocusSession? current = _sessionRuntime.Current;
            if (current is null)
            {
                return;
            }
            bool running = current.State is
                FocusingSessionState
                or OvertimeSessionState
                or LandingSessionState
                or BreakSessionState;
            bool crossedBoundary = projected.State != current.State.Kind;
            if (running && (crossedBoundary || ++_ticksSinceCheckpoint >= CheckpointIntervalTicks))
            {
                _ticksSinceCheckpoint = 0;
                await ExecuteCommandAsync(new RefreshSession(), returnToTodayOnOutcome: false);
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (BreakScreen.Visibility == Visibility.Visible)
            {
                BreakError.Text = "The Break checkpoint could not be saved.";
            }
            else
            {
                FocusControlError.Text = "The focus checkpoint could not be saved.";
                RevealControls();
            }
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

    private void UpdateBreakProjection()
    {
        if (_sessionRuntime?.Current is null)
        {
            return;
        }

        UpdateBreakProjection(_sessionRuntime.GetCurrentView());
    }

    private void UpdateBreakProjection(SessionView view)
    {
        if (view.Timer is not BreakTimerReading timer
            || _sessionRuntime?.Current is not { } current)
        {
            return;
        }

        BreakPlan plan = GetBreakPlan(current.State);
        BreakPromptText.Text = plan.Prompt.Text;
        TimerDisplayParts display = TimerDisplayFormatter.Format(view);
        BreakTimerText.Text = display.ShowHours
            ? $"{display.Hours}:{display.Minutes}:{display.Seconds}"
            : $"{display.Minutes}:{display.Seconds}";
        AutomationProperties.SetName(BreakTimerText, display.AccessibleText);

        TimeSpan returnLead = plan.Duration < TimeSpan.FromMinutes(1)
            ? plan.Duration
            : TimeSpan.FromMinutes(1);
        bool showReturn = timer.Limit - timer.Elapsed <= returnLead;
        BreakReturnContext.Visibility = showReturn
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (showReturn)
        {
            BreakReturningTaskText.Text = _returningTask?.ShortFocusLabel
                ?? "Return to Today";
            BreakReturningActionText.Text = _returningContext?.NextPhysicalAction
                ?? _returningTask?.FirstPhysicalAction
                ?? "Choose the next task after confirming your return.";
        }

        bool requiresRecovery = current.State is RecoveryRequiredSessionState
        {
            InterruptedPhase: ActiveSessionPhase.Break,
        };
        BreakRecoveryPanel.Visibility = requiresRecovery
            ? Visibility.Visible
            : Visibility.Collapsed;
        EndBreakButton.Visibility = requiresRecovery
            ? Visibility.Collapsed
            : Visibility.Visible;
        EndBreakButton.Content = view.State == SessionStateKind.BreakCompleted
            ? "Confirm return"
            : "End Break";
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
                await CompleteAsync();
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
        await CompleteAsync();
    }

    private async System.Threading.Tasks.Task CompleteAsync()
    {
        if (await ExecuteCommandAsync(
                new CompleteSession(),
                returnToTodayOnOutcome: false))
        {
            await PrepareReturningContextAsync(SessionOutcome.Completed);
            await ShowBreakOfferAsync();
        }
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
        var noteInput = new TextBox
        {
            Header = "Short note (optional)",
            TextWrapping = TextWrapping.Wrap,
        };
        var validation = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(nextActionInput, "Next physical action required to park");
        AutomationProperties.SetName(noteInput, "Optional Context Capsule note");
        var content = new StackPanel { MinWidth = 440, Spacing = 12 };
        content.Children.Add(validation);
        content.Children.Add(nextActionInput);
        content.Children.Add(noteInput);
        var dialog = new ContentDialog
        {
            Title = "Park this task",
            Content = content,
            PrimaryButtonText = "Park",
            SecondaryButtonText = "Abandon task",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };

        while (true)
        {
            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None)
            {
                return;
            }

            if (result == ContentDialogResult.Secondary)
            {
                await AbandonAsync();
                return;
            }

            if (!string.IsNullOrWhiteSpace(nextActionInput.Text))
            {
                break;
            }

            validation.Text = "Parking requires a next physical action. Choose Abandon task only if this task will not be resumed.";
        }

        if (await ExecuteCommandAsync(
                new ParkSession(nextActionInput.Text, noteInput.Text),
                returnToTodayOnOutcome: false))
        {
            await PrepareReturningContextAsync(SessionOutcome.Parked);
            await ShowBreakOfferAsync();
        }
    }

    private async System.Threading.Tasks.Task AbandonAsync()
    {
        var confirmation = new ContentDialog
        {
            Title = "Abandon this task?",
            Content = "The task will be deferred without a saved next action. This is distinct from parking it for later.",
            PrimaryButtonText = "Abandon task",
            CloseButtonText = "Keep working",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (await ExecuteCommandAsync(
                new AbandonSession(),
                returnToTodayOnOutcome: false))
        {
            await ShowTodayScreenAsync();
        }
    }

    private async System.Threading.Tasks.Task ShowBreakOfferAsync()
    {
        if (_store is null || _sessionRuntime?.Current is null)
        {
            return;
        }

        try
        {
            BreakSettings settings = await _store.LoadBreakSettingsAsync();
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var duration = new NumberBox
            {
                Header = "Break duration (minutes)",
                Minimum = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = settings.DefaultBreakDuration.TotalMinutes,
                Width = 180,
            };
            AutomationProperties.SetName(duration, "Default Break duration in minutes");
            var prompt = new ComboBox
            {
                Header = "One Break prompt",
                SelectedIndex = 0,
                MinWidth = 280,
            };
            prompt.Items.Add("Distant gaze");
            prompt.Items.Add("Water");
            prompt.Items.Add("Jaw relaxation");
            prompt.Items.Add("Shoulder release");
            prompt.Items.Add("Stand");
            prompt.Items.Add("Walk");
            prompt.Items.Add("User-selected movement");
            AutomationProperties.SetName(prompt, "Break prompt");
            var movement = new TextBox
            {
                Header = "User-selected movement (used only when selected)",
                Text = settings.UserSelectedMovement ?? string.Empty,
            };
            AutomationProperties.SetName(movement, "User-selected movement prompt");
            var content = new StackPanel { MinWidth = 440, Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Landing is focus time; this optional Break is separate and counts up.",
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(error);
            content.Children.Add(duration);
            content.Children.Add(prompt);
            content.Children.Add(movement);
            var dialog = new ContentDialog
            {
                Title = "Take a Break?",
                Content = content,
                PrimaryButtonText = "Start Break",
                SecondaryButtonText = "Return to Today",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
            };

            while (true)
            {
                ContentDialogResult result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    await ShowTodayScreenAsync();
                    return;
                }

                if (!IsValidTimeSpanMinutes(duration.Value))
                {
                    error.Text = "Enter a positive Break duration.";
                    continue;
                }

                var kind = (BreakPromptKind)prompt.SelectedIndex;
                if (kind == BreakPromptKind.UserSelectedMovement
                    && string.IsNullOrWhiteSpace(movement.Text))
                {
                    error.Text = "Enter the movement you want to use for this Break.";
                    continue;
                }

                var updatedSettings = new BreakSettings(
                    TimeSpan.FromMinutes(duration.Value),
                    movement.Text);
                await _store.SaveBreakSettingsAsync(updatedSettings);
                var breakPrompt = new BreakPrompt(
                    kind,
                    kind == BreakPromptKind.UserSelectedMovement ? movement.Text : null);
                bool started = await ExecuteCommandAsync(
                    new BeginBreak(new BreakPlan(
                        updatedSettings.DefaultBreakDuration,
                        breakPrompt)),
                    returnToTodayOnOutcome: false);
                if (started)
                {
                    ShowBreakScreen();
                }

                return;
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("The Break could not be prepared. The saved task outcome is intact.");
            await ShowTodayScreenAsync();
        }
    }

    private async System.Threading.Tasks.Task PrepareReturningContextAsync(
        SessionOutcome outcome)
    {
        _returningTask = null;
        _returningContext = null;
        if (_store is null || _focusedTask is null)
        {
            return;
        }

        if (outcome == SessionOutcome.Parked)
        {
            _returningTask = _focusedTask;
        }
        else
        {
            int focusedIndex = TodayItems
                .Select(item => item.Task.Id)
                .ToList()
                .IndexOf(_focusedTask.Id);
            _returningTask = TodayItems
                .Skip(focusedIndex + 1)
                .Select(item => item.Task)
                .FirstOrDefault(task => task.State is not (TaskState.Completed or TaskState.Deferred));
        }

        if (_returningTask is not null)
        {
            _returningContext = await _store.LoadLatestContextCapsuleAsync(_returningTask.Id);
        }
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

    private async void OnBreakKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.E)
        {
            await EndBreakAsync();
            args.Handled = true;
        }
    }

    private async void OnEndBreakClick(object sender, RoutedEventArgs args)
    {
        await EndBreakAsync();
    }

    private async System.Threading.Tasks.Task EndBreakAsync()
    {
        if (!await ExecuteCommandAsync(new EndBreak(), returnToTodayOnOutcome: false))
        {
            return;
        }

        DomainTask? returningTask = _returningTask;
        await ShowTodayScreenAsync();
        if (returningTask is not null)
        {
            DomainTask currentTask = TodayItems
                .Select(item => item.Task)
                .SingleOrDefault(task => task.Id == returningTask.Id)
                ?? returningTask;
            await ConfirmAndStartTaskAsync(currentTask, requireConfirmation: true);
        }
    }

    private async void OnBreakResumeWithoutAwayClick(object sender, RoutedEventArgs args)
    {
        await ExecuteCommandAsync(
            new ResumeWithoutAwayTime(),
            returnToTodayOnOutcome: false);
    }

    private async void OnBreakResumeIncludingAwayClick(object sender, RoutedEventArgs args)
    {
        if (!double.IsFinite(BreakIncludedAwayMinutes.Value)
            || BreakIncludedAwayMinutes.Value < 0
            || BreakIncludedAwayMinutes.Value >= TimeSpan.MaxValue.TotalMinutes)
        {
            BreakError.Text = "Enter zero or a positive number of minutes.";
            return;
        }

        await ExecuteCommandAsync(
            new ResumeIncludingAwayTime(TimeSpan.FromMinutes(
                BreakIncludedAwayMinutes.Value)),
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
            if (BreakScreen.Visibility == Visibility.Visible)
            {
                UpdateBreakProjection();
            }
            else
            {
                UpdateFocusProjection();
            }
            if (returnToTodayOnOutcome && IsTerminal(transition.Session.State))
            {
                await ShowTodayScreenAsync();
            }
            else if (transition.Session.State is not RecoveryRequiredSessionState
                && command is not RefreshSession)
            {
                if (BreakScreen.Visibility == Visibility.Visible)
                {
                    BreakRecoveryPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    RecoveryPanel.Visibility = Visibility.Collapsed;
                    RevealControls();
                }
            }

            return true;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            string message = command is ResumeIncludingAwayTime
                ? "The included time exceeds the observed time away. Choose a smaller amount."
                : "That action is not available in the current focus state.";
            if (BreakScreen.Visibility == Visibility.Visible)
            {
                BreakError.Text = message;
            }
            else if (_sessionRuntime.Current?.State is RecoveryRequiredSessionState)
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
        return state is
            CompletedSessionState
            or ParkedSessionState
            or AbandonedSessionState
            or DayClosedSessionState;
    }

    private static bool IsBreakState(SessionState state)
    {
        return state is
            BreakSessionState
            or BreakCompletedSessionState
            or RecoveryRequiredSessionState { InterruptedPhase: ActiveSessionPhase.Break };
    }

    private static SessionOutcome GetOutcome(SessionState state)
    {
        return state switch
        {
            CompletedSessionState => SessionOutcome.Completed,
            ParkedSessionState => SessionOutcome.Parked,
            BreakSessionState @break => @break.PriorOutcome,
            BreakCompletedSessionState @break => @break.PriorOutcome,
            RecoveryRequiredSessionState
            {
                InterruptedPhase: ActiveSessionPhase.Break,
                PriorOutcome: { } outcome,
            } => outcome,
            _ => throw new InvalidOperationException(
                $"State '{state.Kind}' does not contain a Break outcome."),
        };
    }

    private static BreakPlan GetBreakPlan(SessionState state)
    {
        return state switch
        {
            BreakSessionState @break => @break.Plan,
            BreakCompletedSessionState @break => @break.Plan,
            RecoveryRequiredSessionState
            {
                InterruptedPhase: ActiveSessionPhase.Break,
                BreakPlan: { } plan,
            } => plan,
            _ => throw new InvalidOperationException(
                $"State '{state.Kind}' does not contain a Break plan."),
        };
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
