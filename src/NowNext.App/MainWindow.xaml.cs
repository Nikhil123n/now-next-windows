using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NowNext.App.Persistence;
using NowNext.App.Presentation;
using NowNext.App.WindowsIntegration;
using NowNext.Core.Domain;
using NowNext.Core.Planning;
using NowNext.Core.Sessions;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.App;

public sealed partial class MainWindow : Window
{
    private const int ControlsInactivitySeconds = 6;
    private const int CheckpointIntervalTicks = 20;
    private static readonly TimeSpan SubstantialAbsenceThreshold = TimeSpan.FromMinutes(15);

    private readonly TodayPlanStore? _store;
    private readonly FocusSessionRuntime? _sessionRuntime;
    private readonly IKeepAwakeController? _keepAwakeController;
    private readonly WindowsLifecycleCoordinator? _lifecycleCoordinator;
    private readonly DataMaintenanceService? _dataMaintenanceService;
    private readonly IWindowsUserSettings? _userSettings;
    private readonly ILaunchAtSignInService? _launchAtSignInService;
    private readonly IReducedMotionPreference? _reducedMotionPreference;
    private readonly DispatcherQueueTimer _focusProjectionTimer;
    private readonly DispatcherQueueTimer _colonTimer;
    private readonly DispatcherQueueTimer _controlsInactivityTimer;
    private AppWindow? _appWindow;
    private DomainTask? _editingTask;
    private DomainTask? _focusedTask;
    private DomainTask? _returningTask;
    private ContextCapsule? _returningContext;
    private WorkdaySnapshot? _workdaySnapshot;
    private ScheduleRepairProposal? _pendingRepair;
    private bool _commandInProgress;
    private bool _colonVisible = true;
    private bool _allowClose;
    private bool _closeCheckpointInProgress;
    private int _ticksSinceCheckpoint;
    private long _lastProjectionTimestamp;

    public MainWindow(
        TodayPlanStore store,
        FocusSessionRuntime sessionRuntime,
        IKeepAwakeController keepAwakeController,
        WindowsLifecycleCoordinator lifecycleCoordinator,
        DataMaintenanceService dataMaintenanceService,
        IWindowsUserSettings userSettings,
        ILaunchAtSignInService launchAtSignInService,
        IReducedMotionPreference reducedMotionPreference)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sessionRuntime = sessionRuntime ?? throw new ArgumentNullException(nameof(sessionRuntime));
        _keepAwakeController = keepAwakeController
            ?? throw new ArgumentNullException(nameof(keepAwakeController));
        _lifecycleCoordinator = lifecycleCoordinator
            ?? throw new ArgumentNullException(nameof(lifecycleCoordinator));
        _dataMaintenanceService = dataMaintenanceService
            ?? throw new ArgumentNullException(nameof(dataMaintenanceService));
        _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        _launchAtSignInService = launchAtSignInService
            ?? throw new ArgumentNullException(nameof(launchAtSignInService));
        _reducedMotionPreference = reducedMotionPreference
            ?? throw new ArgumentNullException(nameof(reducedMotionPreference));
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
        _lastProjectionTimestamp = TimeProvider.System.GetTimestamp();
    }

    public MainWindow(string statusMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMessage);
        InitializeComponent();
        _focusProjectionTimer = DispatcherQueue.CreateTimer();
        _colonTimer = DispatcherQueue.CreateTimer();
        _controlsInactivityTimer = DispatcherQueue.CreateTimer();
        _lastProjectionTimestamp = TimeProvider.System.GetTimestamp();
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

    internal async System.Threading.Tasks.Task HandleRecoveryReloadedAsync()
    {
        if (_sessionRuntime?.Current is not { } current || _store is null)
        {
            return;
        }

        try
        {
            await ReloadTodayAsync();
            _focusedTask = TodayItems
                .Select(item => item.Task)
                .SingleOrDefault(task => task.Id == current.TaskId);
            if (_focusedTask is null)
            {
                SetStatus("The saved focus session does not match today's plan.");
                return;
            }

            if (IsBreakState(current.State))
            {
                await PrepareReturningContextAsync(GetOutcome(current.State));
                ShowBreakScreen();
            }
            else if (current.State is RecoveryRequiredSessionState)
            {
                ShowFocusScreen();
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("NOW/NEXT could not restore the recovery view after resume.");
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
            await LoadWindowsSettingsAsync();
            await ReloadTodayAsync();
            if (_workdaySnapshot?.Closure is { } closure)
            {
                ShowRestingScreen(closure);
                return;
            }

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
        if (_userSettings?.StartFullScreen == true)
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
    }

    private async System.Threading.Tasks.Task LoadWindowsSettingsAsync()
    {
        if (_userSettings is null
            || _launchAtSignInService is null
            || _reducedMotionPreference is null)
        {
            WindowsDataSettingsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        KeepDisplayAwakeCheckBox.IsChecked = _userSettings.KeepDisplayAwakeDuringSessions;
        StartFullScreenCheckBox.IsChecked = _userSettings.StartFullScreen;
        ReducedMotionStatusText.Text = _reducedMotionPreference.IsReducedMotionEnabled
            ? "Windows Reduced Motion is on; the timer colon stays visible."
            : "Windows Reduced Motion is off; the timer colon blinks once per second.";

        try
        {
            ApplyLaunchAtSignInState(await _launchAtSignInService.GetStateAsync());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UnauthorizedAccessException
                or System.Runtime.InteropServices.ExternalException)
        {
            LaunchAtSignInCheckBox.IsChecked = false;
            LaunchAtSignInCheckBox.IsEnabled = false;
            LaunchAtSignInCheckBox.Content = "Launch at sign-in is unavailable";
        }
    }

    private async void OnLaunchAtSignInClick(object sender, RoutedEventArgs args)
    {
        if (_launchAtSignInService is null)
        {
            return;
        }

        try
        {
            bool enabled = LaunchAtSignInCheckBox.IsChecked == true;
            LaunchAtSignInState state = await _launchAtSignInService.SetEnabledAsync(enabled);
            ApplyLaunchAtSignInState(state);
            DataMaintenanceStatusText.Text = state switch
            {
                LaunchAtSignInState.Enabled or LaunchAtSignInState.EnabledByPolicy =>
                    "NOW/NEXT will launch after Windows sign-in.",
                LaunchAtSignInState.DisabledByUser =>
                    "Launch at sign-in is disabled in Windows Task Manager.",
                LaunchAtSignInState.DisabledByPolicy =>
                    "Launch at sign-in is disabled by Windows policy.",
                _ => "Launch at sign-in is off.",
            };
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or UnauthorizedAccessException
                or System.Runtime.InteropServices.ExternalException)
        {
            DataMaintenanceStatusText.Text = "Windows could not change launch at sign-in.";
            await LoadWindowsSettingsAsync();
        }
    }

    private void OnKeepDisplayAwakeClick(object sender, RoutedEventArgs args)
    {
        if (_userSettings is null || _sessionRuntime is null)
        {
            return;
        }

        _userSettings.KeepDisplayAwakeDuringSessions =
            KeepDisplayAwakeCheckBox.IsChecked == true;
        _sessionRuntime.RefreshKeepAwake();
        DataMaintenanceStatusText.Text = _userSettings.KeepDisplayAwakeDuringSessions
            ? "The display stays awake only while an active session is accruing time."
            : "Display keep-awake is off.";
    }

    private void OnStartFullScreenClick(object sender, RoutedEventArgs args)
    {
        if (_userSettings is null)
        {
            return;
        }

        _userSettings.StartFullScreen = StartFullScreenCheckBox.IsChecked == true;
        DataMaintenanceStatusText.Text = _userSettings.StartFullScreen
            ? "NOW/NEXT will open full screen on its next launch."
            : "NOW/NEXT will use a normal window on its next launch.";
    }

    private async void OnBackupClick(object sender, RoutedEventArgs args)
    {
        if (_dataMaintenanceService is null || _sessionRuntime is null)
        {
            return;
        }

        try
        {
            await _sessionRuntime.PersistRecoveryCheckpointAsync();
            string backupPath = await _dataMaintenanceService.CreateBackupAsync();
            DataMaintenanceStatusText.Text =
                $"Validated local backup created: {Path.GetFileName(backupPath)}";
        }
        catch (Exception exception) when (IsDataMaintenanceFailure(exception))
        {
            DataMaintenanceStatusText.Text = "The local backup could not be created.";
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs args)
    {
        if (_dataMaintenanceService is null || _sessionRuntime is null)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = "now-next-export",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeChoices.Add("SQLite database", [".db"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? selectedFile = await picker.PickSaveFileAsync();
        if (selectedFile is null)
        {
            return;
        }

        try
        {
            await _sessionRuntime.PersistRecoveryCheckpointAsync();
            await _dataMaintenanceService.ExportAsync(selectedFile.Path);
            DataMaintenanceStatusText.Text = "A validated local export was created.";
        }
        catch (Exception exception) when (IsDataMaintenanceFailure(exception))
        {
            DataMaintenanceStatusText.Text = "The local export could not be created.";
        }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs args)
    {
        if (_dataMaintenanceService is null || _sessionRuntime is null)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".db");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? selectedFile = await picker.PickSingleFileAsync();
        if (selectedFile is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Restore local data?",
            Content = new TextBlock
            {
                Text = "The selected database will be validated before it replaces current local data. Active work will return through Recovery Mode.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Validate and restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ReplaceLocalDataAsync(
            async () => await _dataMaintenanceService.RestoreAsync(selectedFile.Path),
            "Local data was restored. Review Recovery Mode before continuing.",
            "The selected database was not restored.");
    }

    private async void OnResetDataClick(object sender, RoutedEventArgs args)
    {
        if (_dataMaintenanceService is null
            || _sessionRuntime is null
            || _userSettings is null
            || _launchAtSignInService is null)
        {
            return;
        }

        var confirmation = new CheckBox
        {
            Content = "I understand this removes tasks, sessions, settings, backups, exports, and diagnostics from this app.",
            IsChecked = false,
            MinHeight = 44,
        };
        AutomationProperties.SetName(
            confirmation,
            "Confirm permanent removal of all local NOW/NEXT data");
        var dialog = new ContentDialog
        {
            Title = "Completely reset local data?",
            Content = confirmation,
            PrimaryButtonText = "Reset all local data",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            XamlRoot = RootGrid.XamlRoot,
        };
        confirmation.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        confirmation.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ReplaceLocalDataAsync(
            async () => await _dataMaintenanceService.ResetAsync(
                _userSettings,
                _launchAtSignInService),
            "All prior local data was removed. NOW/NEXT is ready for a fresh day.",
            "Local data was not reset.");
    }

    private async System.Threading.Tasks.Task ReplaceLocalDataAsync(
        Func<System.Threading.Tasks.Task> replaceOperation,
        string successMessage,
        string failureMessage)
    {
        if (_sessionRuntime is null)
        {
            return;
        }

        _commandInProgress = true;
        _focusProjectionTimer.Stop();
        _colonTimer.Stop();
        _controlsInactivityTimer.Stop();
        try
        {
            await _sessionRuntime.InterruptForSuspensionAsync();
            await replaceOperation();
            await _sessionRuntime.ReloadAfterResumeAsync();
            await ReloadTodayAsync();
            if (_workdaySnapshot?.Closure is { } closure)
            {
                ShowRestingScreen(closure);
            }
            else if (_sessionRuntime.Current?.State is RecoveryRequiredSessionState)
            {
                await HandleRecoveryReloadedAsync();
            }
            else
            {
                await ShowTodayScreenAsync();
            }

            DataMaintenanceStatusText.Text = successMessage;
            await LoadWindowsSettingsAsync();
        }
        catch (Exception exception) when (IsDataMaintenanceFailure(exception))
        {
            DataMaintenanceStatusText.Text = failureMessage;
            try
            {
                await _sessionRuntime.ReloadAfterResumeAsync();
                if (_sessionRuntime.Current?.State is RecoveryRequiredSessionState)
                {
                    await HandleRecoveryReloadedAsync();
                }
            }
            catch (Exception recoveryException) when (IsExpectedFailure(recoveryException))
            {
                SetStatus("NOW/NEXT could not reload recovery state after local data maintenance.");
            }
        }
        finally
        {
            _commandInProgress = false;
        }
    }

    private void ApplyLaunchAtSignInState(LaunchAtSignInState state)
    {
        LaunchAtSignInCheckBox.IsChecked = state is
            LaunchAtSignInState.Enabled or LaunchAtSignInState.EnabledByPolicy;
        LaunchAtSignInCheckBox.IsEnabled = state is
            LaunchAtSignInState.Disabled or LaunchAtSignInState.Enabled;
        LaunchAtSignInCheckBox.Content = state switch
        {
            LaunchAtSignInState.DisabledByUser =>
                "Launch at sign-in (disabled in Windows Task Manager)",
            LaunchAtSignInState.DisabledByPolicy or LaunchAtSignInState.EnabledByPolicy =>
                "Launch at sign-in (managed by Windows policy)",
            _ => "Launch at sign-in",
        };
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || _closeCheckpointInProgress || _lifecycleCoordinator is null)
        {
            return;
        }

        FocusSession? current = _sessionRuntime?.Current;
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
            await _lifecycleCoordinator.PersistBeforeExitAsync();
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
        FixedUnlockConfirmation.IsChecked = false;
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
        FixedUnlockConfirmation.IsChecked = false;
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

            bool changesProtectedFixed = _editingTask?.ScheduleType == ScheduleType.Fixed
                && (_editingTask.PlannedStart != task!.PlannedStart
                    || _editingTask.PlannedDuration != task.PlannedDuration
                    || task.ScheduleType != ScheduleType.Fixed);
            if (changesProtectedFixed && FixedUnlockConfirmation.IsChecked != true)
            {
                TaskEditorError.Text =
                    "Confirm the explicit Fixed-commitment unlock before saving this change.";
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
        RestingScreen.Visibility = Visibility.Collapsed;
        BreakScreen.Visibility = Visibility.Collapsed;
        FocusScreen.Visibility = Visibility.Visible;
        FocusLabelText.Text = _focusedTask.ShortFocusLabel;
        FocusControls.Visibility = Visibility.Collapsed;
        FocusControlError.Text = string.Empty;
        _colonVisible = true;
        TimerFirstColonText.Opacity = 1;
        TimerSecondColonText.Opacity = 1;
        _ticksSinceCheckpoint = 0;
        _lastProjectionTimestamp = TimeProvider.System.GetTimestamp();
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
        RestingScreen.Visibility = Visibility.Collapsed;
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
        _lastProjectionTimestamp = TimeProvider.System.GetTimestamp();
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
            long observedTimestamp = TimeProvider.System.GetTimestamp();
            TimeSpan observationGap = TimeProvider.System.GetElapsedTime(
                _lastProjectionTimestamp,
                observedTimestamp);
            _lastProjectionTimestamp = observedTimestamp;
            if (observationGap >= SubstantialAbsenceThreshold
                && _sessionRuntime.Current.State is
                    FocusingSessionState
                    or OvertimeSessionState
                    or LandingSessionState
                    or BreakSessionState)
            {
                await _sessionRuntime.ReloadForSubstantialAbsenceAsync();
                if (BreakScreen.Visibility == Visibility.Visible)
                {
                    UpdateBreakProjection();
                }
                else
                {
                    UpdateFocusProjection();
                }

                return;
            }

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
            UpdateRecoveryOverview(current);
        }
    }

    private void UpdateRecoveryOverview(FocusSession current)
    {
        if (_workdaySnapshot?.Settings is not { } settings
            || _workdaySnapshot.Plan.Entries.All(entry => entry.Task.Id != current.TaskId))
        {
            RecoveryNextFixedText.Text = "Configure protected shutdown on Today to rebuild the day.";
            RecoveryAvailableTimeText.Text = string.Empty;
            return;
        }

        DateTimeOffset localNow = TimeProvider.System.GetLocalNow();
        RecoveryOverview overview = WorkdayProjections.CreateRecoveryOverview(
            _workdaySnapshot.Plan,
            current.TaskId,
            TimeOnly.FromDateTime(localNow.DateTime),
            settings.ShutdownTime);
        RecoveryNextFixedText.Text = overview.NextFixedTaskId is null
            ? "Next Fixed commitment: none remaining."
            : $"Next Fixed commitment: {GetTaskLabel(overview.NextFixedTaskId.Value)} at "
                + overview.NextFixedStart!.Value.ToString("t", CultureInfo.CurrentCulture)
                + ".";
        RecoveryAvailableTimeText.Text =
            $"Realistically available before the next protected boundary: "
            + $"{FormatDuration(overview.RealisticAvailableTime)}.";
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
        _colonVisible = TimerColonPolicy.GetNextVisibility(
            _colonVisible,
            _reducedMotionPreference?.IsReducedMotionEnabled == true);

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
        TimeSpan extension = TimeSpan.FromMinutes(minutes);
        if (await ExecuteCommandAsync(
                new ExtendSession(extension),
                returnToTodayOnOutcome: false))
        {
            await ReviewRepairAsync(
                ScheduleRepairTriggerKind.SessionExtended,
                extension);
        }
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

    private async void OnSaveDaySettingsClick(object sender, RoutedEventArgs args)
    {
        if (_store is null || _workdaySnapshot is null)
        {
            return;
        }

        try
        {
            if (ShutdownTimeInput.SelectedTime is not { } selectedShutdown)
            {
                SetStatus("Choose today's protected shutdown time explicitly.");
                return;
            }

            TimeOnly shutdown = TimeOnly.FromTimeSpan(selectedShutdown);
            if (_workdaySnapshot.Settings is { } existing
                && existing.ShutdownTime != shutdown)
            {
                var confirmation = new ContentDialog
                {
                    Title = "Change protected shutdown?",
                    Content =
                        $"This explicitly changes shutdown from "
                        + $"{existing.ShutdownTime.ToString("t", CultureInfo.CurrentCulture)} "
                        + $"to {shutdown.ToString("t", CultureInfo.CurrentCulture)}.",
                    PrimaryButtonText = "Change protected time",
                    CloseButtonText = "Keep current time",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = RootGrid.XamlRoot,
                };
                if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                {
                    ShutdownTimeInput.SelectedTime = existing.ShutdownTime.ToTimeSpan();
                    return;
                }
            }

            TaskId? dailyWinTaskId = (DailyWinInput.SelectedItem as ComboBoxItem)?.Tag
                is TaskId selected
                    ? selected
                    : null;
            await _store.SaveDaySettingsAsync(new DaySettings(
                _workdaySnapshot.Plan.Date,
                shutdown,
                dailyWinTaskId));
            await ReloadTodayAsync();
            StatusText.Text = "Protected shutdown and optional Daily Win saved.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("Today's protected settings could not be saved.");
        }
    }

    private async void OnReviewRepairClick(object sender, RoutedEventArgs args)
    {
        await ReviewRepairAsync(ScheduleRepairTriggerKind.CurrentTime, extension: null);
    }

    private async System.Threading.Tasks.Task ReviewRepairAsync(
        ScheduleRepairTriggerKind triggerKind,
        TimeSpan? extension)
    {
        if (_store is null || _workdaySnapshot is null)
        {
            return;
        }

        if (_workdaySnapshot.Settings is null)
        {
            SetStatus("Configure today's protected shutdown before reviewing repair.");
            return;
        }

        try
        {
            _workdaySnapshot = await _store.LoadWorkdaySnapshotAsync();
            _pendingRepair = CreateRepairProposal(_workdaySnapshot, triggerKind, extension);
            ScheduleRepairProposal proposal = _pendingRepair;
            var explanation = new StringBuilder();
            string extensionDescription = proposal.Request.Trigger.Extension is
            { } recordedExtension
                    ? $" ({FormatDuration(recordedExtension)})"
                    : string.Empty;
            explanation.AppendLine(string.Format(
                CultureInfo.CurrentCulture,
                "Trigger: {0}{1}. Buffer consumed: {2}.",
                proposal.Request.Trigger.Kind,
                extensionDescription,
                FormatDuration(proposal.BufferConsumed)));
            foreach (BufferConsumption consumption in proposal.BufferConsumptions)
            {
                string boundary = consumption.BeforeTaskId is { } beforeTaskId
                    ? $"before {GetTaskLabel(beforeTaskId)}"
                    : "before protected shutdown";
                explanation.AppendLine(
                    CultureInfo.CurrentCulture,
                    $"Buffer {boundary}: {FormatDuration(consumption.Duration)}.");
            }

            foreach (ScheduleMove move in proposal.Moves)
            {
                explanation.AppendLine(
                    CultureInfo.CurrentCulture,
                    $"Move {GetTaskLabel(move.TaskId)} from "
                    + $"{move.OriginalStart.ToString("t", CultureInfo.CurrentCulture)} to "
                    + $"{move.RevisedStart.ToString("t", CultureInfo.CurrentCulture)}.");
            }

            if (proposal.Deferral is { } deferral)
            {
                explanation.AppendLine(
                    CultureInfo.CurrentCulture,
                    $"Defer {GetTaskLabel(deferral.TaskId)}.");
            }

            explanation.AppendLine(
                CultureInfo.CurrentCulture,
                $"Revised finish: {FormatClockOffset(proposal.RevisedFinishFromMidnight)}.");
            if (proposal.Overflow > TimeSpan.Zero)
            {
                explanation.AppendLine(
                    CultureInfo.CurrentCulture,
                    $"Remaining overflow: {FormatDuration(proposal.Overflow)}.");
            }

            if (proposal.ProtectedFixedCommitments.Count == 0)
            {
                explanation.AppendLine("Protected Fixed commitments: none.");
            }
            else
            {
                explanation.AppendLine("Protected Fixed commitments:");
                foreach (ProtectedFixedCommitment protection in proposal.ProtectedFixedCommitments)
                {
                    explanation.AppendLine(
                        CultureInfo.CurrentCulture,
                        $"- {GetTaskLabel(protection.TaskId)} at {protection.Start.ToString("t", CultureInfo.CurrentCulture)}");
                }
            }

            explanation.AppendLine(
                $"Protected shutdown remains "
                + proposal.Request.ShutdownTime.ToString("t", CultureInfo.CurrentCulture)
                + ".");
            if (proposal.Status == ScheduleRepairStatus.Impossible)
            {
                explanation.AppendLine(
                    CultureInfo.CurrentCulture,
                    $"No automatic apply is available: {DescribeRepairIssue(proposal.Issue)}");
            }

            var dialog = new ContentDialog
            {
                Title = proposal.Status == ScheduleRepairStatus.Impossible
                    ? "The day needs a manual decision"
                    : proposal.Status == ScheduleRepairStatus.NoChange
                        ? "The plan still fits"
                        : "One recommended repair",
                Content = new ScrollViewer
                {
                    MaxHeight = 520,
                    Content = new TextBlock
                    {
                        Text = explanation.ToString(),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
                PrimaryButtonText = proposal.CanApply ? "Accept repair" : string.Empty,
                CloseButtonText = "Keep current plan",
                DefaultButton = proposal.CanApply
                    ? ContentDialogButton.Primary
                    : ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await _store.ApplyScheduleRepairAsync(proposal);
                await ReloadTodayAsync();
                StatusText.Text = "The approved repair was applied.";
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("The repair could not be reviewed. Reload Today and try again.");
        }
    }

    private async void OnUndoRepairClick(object sender, RoutedEventArgs args)
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            bool undone = await _store.UndoLatestScheduleRepairAsync();
            await ReloadTodayAsync();
            StatusText.Text = undone
                ? "The latest accepted repair was undone."
                : "There is no accepted repair available to undo.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("The latest repair cannot be undone after its affected tasks changed.");
        }
    }

    private async void OnShutdownClick(object sender, RoutedEventArgs args)
    {
        await ShowShutdownAsync();
    }

    private async System.Threading.Tasks.Task ShowShutdownAsync()
    {
        if (_store is null || _sessionRuntime is null || _keepAwakeController is null)
        {
            return;
        }

        if (_workdaySnapshot?.Settings is null)
        {
            SetStatus("Configure today's protected shutdown before closing the workday.");
            return;
        }

        FocusSession? current = _sessionRuntime.Current;
        if (current?.State is not (
            ReadySessionState
            or CompletedSessionState
            or ParkedSessionState
            or AbandonedSessionState
            or BreakSessionState
            or BreakCompletedSessionState
            or DayClosedSessionState)
            && current is not null)
        {
            SetStatus("Resolve the interrupted or active focus session before Shutdown.");
            return;
        }

        try
        {
            ShutdownSummary summary = await _store.CreateShutdownSummaryAsync();
            string completed = summary.Completed.Count == 0
                ? "None"
                : string.Join(", ", summary.Completed.Select(item => GetTaskLabel(item.TaskId)));
            string deferred = summary.Deferred.Count == 0
                ? "None"
                : string.Join(", ", summary.Deferred.Select(item => GetTaskLabel(item.TaskId)));
            string dailyWin = summary.DailyWinStatus switch
            {
                DailyWinStatus.NotSelected => "No Daily Win selected",
                DailyWinStatus.Completed => "Daily Win completed",
                DailyWinStatus.NotCompleted => "Daily Win not completed",
                _ => throw new InvalidOperationException("Daily Win status is invalid."),
            };
            var content = new TextBlock
            {
                Text =
                    $"Completed: {completed}\n"
                    + $"Deliberately deferred: {deferred}\n"
                    + $"Planned: {FormatDuration(summary.TotalPlannedDuration)}; "
                    + $"actual focused: {FormatDuration(summary.TotalActualDuration)}\n"
                    + $"{dailyWin}\n"
                    + (summary.NextUnfinishedTaskId is null
                        ? "No unfinished task remains."
                        : $"Next action for {GetTaskLabel(summary.NextUnfinishedTaskId.Value)}: "
                            + summary.NextPhysicalAction),
                TextWrapping = TextWrapping.Wrap,
            };
            var dialog = new ContentDialog
            {
                Title = "Close the workday?",
                Content = content,
                PrimaryButtonText = "Confirm Shutdown",
                CloseButtonText = "Keep day open",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            DayClosure closure = await _sessionRuntime.CloseDayAsync(
                summary,
                _keepAwakeController);
            ShowRestingScreen(closure);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            SetStatus("The workday could not be closed. The day remains open.");
        }
    }

    private async void OnRecoveryRebuildClick(object sender, RoutedEventArgs args)
    {
        await ReviewRepairAsync(ScheduleRepairTriggerKind.RecoveryRebuild, extension: null);
    }

    private async void OnRecoveryEndClick(object sender, RoutedEventArgs args)
    {
        await ResolveInterruptedSessionAsync(closeAfter: false);
    }

    private async void OnRecoveryCloseEarlyClick(object sender, RoutedEventArgs args)
    {
        if (_sessionRuntime?.Current?.State is RecoveryRequiredSessionState
            {
                InterruptedPhase: ActiveSessionPhase.Break,
            })
        {
            if (await ExecuteCommandAsync(new EndBreak(), returnToTodayOnOutcome: false))
            {
                await ShowShutdownAsync();
            }

            return;
        }

        await ResolveInterruptedSessionAsync(closeAfter: true);
    }

    private async System.Threading.Tasks.Task ResolveInterruptedSessionAsync(bool closeAfter)
    {
        var outcome = new ComboBox
        {
            Header = "How should the interrupted task end?",
            MinWidth = 300,
            SelectedIndex = 0,
            Items =
            {
                "Done",
                "Park with a next action",
                "Abandon explicitly",
            },
        };
        var nextAction = new TextBox
        {
            Header = "Next physical action (required for Park)",
            Text = _focusedTask?.NextPhysicalAction ?? string.Empty,
        };
        var note = new TextBox
        {
            Header = "Short Context Capsule note (optional)",
        };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { MinWidth = 440, Spacing = 12 };
        panel.Children.Add(error);
        panel.Children.Add(outcome);
        panel.Children.Add(nextAction);
        panel.Children.Add(note);
        var dialog = new ContentDialog
        {
            Title = "End interrupted session",
            Content = panel,
            PrimaryButtonText = "Save outcome",
            CloseButtonText = "Keep session",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };
        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            SessionCommand command;
            if (outcome.SelectedIndex == 0)
            {
                command = new CompleteSession();
            }
            else if (outcome.SelectedIndex == 1)
            {
                if (string.IsNullOrWhiteSpace(nextAction.Text))
                {
                    error.Text = "Parking requires a next physical action.";
                    continue;
                }

                command = new ParkSession(nextAction.Text, note.Text);
            }
            else
            {
                command = new AbandonSession();
            }

            if (!await ExecuteCommandAsync(command, returnToTodayOnOutcome: false))
            {
                return;
            }

            if (closeAfter)
            {
                await ShowShutdownAsync();
            }
            else
            {
                await ShowTodayScreenAsync();
            }

            return;
        }
    }

    private string GetTaskLabel(TaskId taskId)
    {
        return TodayItems
            .Select(item => item.Task)
            .SingleOrDefault(task => task.Id == taskId)
            ?.FullTitle ?? $"task {taskId}";
    }

    private void ShowRestingScreen(DayClosure closure)
    {
        _focusProjectionTimer.Stop();
        _colonTimer.Stop();
        _controlsInactivityTimer.Stop();
        TodayScreen.Visibility = Visibility.Collapsed;
        RestingScreen.Visibility = Visibility.Collapsed;
        FocusScreen.Visibility = Visibility.Collapsed;
        BreakScreen.Visibility = Visibility.Collapsed;
        RestingScreen.Visibility = Visibility.Visible;
        RestingSummaryText.Text =
            $"Closed {closure.ClosedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}. "
            + $"Planned {FormatDuration(closure.TotalPlannedDuration)}; "
            + $"actual focused {FormatDuration(closure.TotalActualDuration)}.";
        _appWindow?.SetPresenter(AppWindowPresenterKind.Default);
    }

    private async System.Threading.Tasks.Task ReloadTodayAsync()
    {
        if (_store is null)
        {
            return;
        }

        WorkdaySnapshot snapshot = await _store.LoadWorkdaySnapshotAsync();
        _workdaySnapshot = snapshot;
        TodayPlan plan = snapshot.Plan;
        TodayItems.Clear();
        foreach (ScheduleEntry entry in plan.Entries)
        {
            TodayItems.Add(new TodayTaskItem(entry));
        }

        EmptyTodayPanel.Visibility = TodayItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        TodayDateText.Text = plan.Date.ToString("D", CultureInfo.CurrentCulture);
        PopulateDaySettings(snapshot);
        SetDayMutationAvailability(snapshot.Closure is null);
        if (snapshot.Closure is null && snapshot.Settings is not null)
        {
            _pendingRepair = CreateRepairProposal(
                snapshot,
                ScheduleRepairTriggerKind.CurrentTime,
                extension: null);
            RepairCallout.Visibility = _pendingRepair.Status == ScheduleRepairStatus.NoChange
                ? Visibility.Collapsed
                : Visibility.Visible;
            RepairCalloutText.Text = DescribeRepairStatus(_pendingRepair);
        }
        else
        {
            _pendingRepair = null;
            RepairCallout.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateDaySettings(WorkdaySnapshot snapshot)
    {
        ShutdownTimeInput.SelectedTime = snapshot.Settings?.ShutdownTime.ToTimeSpan();
        DailyWinInput.Items.Clear();
        DailyWinInput.Items.Add(new ComboBoxItem
        {
            Content = "No Daily Win selected",
            Tag = null,
        });
        int selectedIndex = 0;
        foreach (TodayTaskItem item in TodayItems)
        {
            int index = DailyWinInput.Items.Count;
            DailyWinInput.Items.Add(new ComboBoxItem
            {
                Content = item.Task.FullTitle,
                Tag = item.Task.Id,
            });
            if (snapshot.Settings?.DailyWinTaskId == item.Task.Id)
            {
                selectedIndex = index;
            }
        }

        DailyWinInput.SelectedIndex = selectedIndex;
    }

    private void SetDayMutationAvailability(bool isOpen)
    {
        AddTaskButton.IsEnabled = isOpen;
        TodayTaskList.IsEnabled = isOpen;
        DayControls.IsHitTestVisible = isOpen;
        DayControls.Opacity = isOpen ? 1 : 0.56;
    }

    private ScheduleRepairProposal CreateRepairProposal(
        WorkdaySnapshot snapshot,
        ScheduleRepairTriggerKind triggerKind,
        TimeSpan? extension)
    {
        DaySettings settings = snapshot.Settings
            ?? throw new InvalidOperationException(
                "Configure today's protected shutdown before reviewing repair.");
        DateTimeOffset localNow = TimeProvider.System.GetLocalNow();
        FocusSession? session = _sessionRuntime?.Current;
        TaskId? currentTaskId = session?.State is
            ReadySessionState
            or CompletedSessionState
            or ParkedSessionState
            or AbandonedSessionState
            or DayClosedSessionState
                ? null
                : session?.TaskId;
        TimeSpan remaining = currentTaskId is null || _sessionRuntime is null
            ? TimeSpan.Zero
            : GetRemainingDuration(_sessionRuntime.GetCurrentView());
        return ScheduleRepairEngine.Propose(new ScheduleRepairRequest(
            new ScheduleRepairId(Guid.NewGuid()),
            snapshot.ScheduleRevision,
            snapshot.Plan,
            TimeOnly.FromDateTime(localNow.DateTime),
            settings.ShutdownTime,
            new ScheduleRepairTrigger(
                triggerKind,
                localNow.ToUniversalTime(),
                extension),
            currentTaskId,
            remaining));
    }

    private static TimeSpan GetRemainingDuration(SessionView view)
    {
        return view.Timer switch
        {
            CountUpTimerReading countUp when countUp.Elapsed < countUp.Limit =>
                countUp.Limit - countUp.Elapsed,
            CountdownTimerReading countdown => countdown.Remaining,
            LandingTimerReading landing when landing.Elapsed < landing.Limit =>
                landing.Limit - landing.Elapsed,
            BreakTimerReading @break when @break.Elapsed < @break.Limit =>
                @break.Limit - @break.Elapsed,
            _ => TimeSpan.Zero,
        };
    }

    private static string DescribeRepairStatus(ScheduleRepairProposal proposal)
    {
        return proposal.Status switch
        {
            ScheduleRepairStatus.NoChange =>
                $"The plan still fits; {FormatDuration(proposal.BufferConsumed)} of buffer is consumed.",
            ScheduleRepairStatus.RequiresApproval =>
                $"{proposal.Moves.Count} task move(s), "
                + $"{(proposal.Deferral is null ? "no deferral" : "one deferral")}, "
                + $"finishing at {FormatClockOffset(proposal.RevisedFinishFromMidnight)}.",
            ScheduleRepairStatus.Impossible =>
                $"The protected plan needs a manual decision: {DescribeRepairIssue(proposal.Issue)}",
            _ => throw new InvalidOperationException("Schedule repair status is invalid."),
        };
    }

    private static string DescribeRepairIssue(ScheduleRepairIssue issue)
    {
        return issue switch
        {
            ScheduleRepairIssue.FixedCommitmentsOverlap =>
                "Fixed commitments overlap.",
            ScheduleRepairIssue.CurrentSessionOverlapsFixed =>
                "the current session would overlap a Fixed commitment.",
            ScheduleRepairIssue.FixedCommitmentMissed =>
                "an unfinished Fixed commitment has already started.",
            ScheduleRepairIssue.FixedCommitmentExceedsShutdown =>
                "a Fixed commitment extends beyond protected shutdown.",
            ScheduleRepairIssue.ScheduleCrossesMidnight =>
                "remaining work crosses midnight.",
            ScheduleRepairIssue.ShutdownHasPassed =>
                "protected shutdown has already passed.",
            ScheduleRepairIssue.InsufficientFlexibleTime =>
                "deferring the one recommended Flexible task is not enough.",
            _ => "the remaining plan cannot be repaired automatically.",
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalMinutes < 1
            ? "0 minutes"
            : $"{Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero)} minutes";
    }

    private static string FormatClockOffset(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero || offset >= TimeSpan.FromDays(1))
        {
            return "outside today";
        }

        return DateTime.Today.Add(offset).ToString("t", CultureInfo.CurrentCulture);
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

    private static bool IsDataMaintenanceFailure(Exception exception)
    {
        return IsExpectedFailure(exception)
            || exception is
                DataMaintenanceException
                or IOException
                or UnauthorizedAccessException
                or System.Runtime.InteropServices.ExternalException;
    }

    private static bool IsValidTimeSpanMinutes(double minutes)
    {
        return double.IsFinite(minutes)
            && minutes > 0
            && minutes < TimeSpan.MaxValue.TotalMinutes;
    }
}
