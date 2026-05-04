// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Gausslite.App.Diagnostics;
using Gausslite.App.Orchestration;
using Gausslite.App.Persistence;
using Gausslite.Core.Blur;
using Gausslite.Core.ScreenShare;
using Gausslite.Core.Settings;

namespace Gausslite.App.Tray;

/// <summary>
/// Owns the system-tray <see cref="TaskbarIcon"/> and keeps its "Enable blur" checked
/// state in sync with <see cref="ITrayOrchestrator.IsBlurEnabled"/>.
/// Must be created and disposed on the WPF UI thread.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly ITrayOrchestrator _orchestrator;
    private readonly TrayNotifier? _notifier;
    private readonly ISettingsStore _settingsStore;
    private readonly IAutoStartManager _autoStart;
    private readonly WindowSignalScreenShareDetector _detector;
    private Settings _settings;
    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _toggleItem;
    private MenuItem? _intensityLightItem;
    private MenuItem? _intensityMediumItem;
    private MenuItem? _intensityHeavyItem;
    private MenuItem? _scopeChatListItem;
    private MenuItem? _scopeConversationItem;
    private MenuItem? _scopeBothItem;
    private MenuItem? _autoStartItem;
    private MenuItem? _processHeuristicItem;

    public TrayIconHost(
        ITrayOrchestrator orchestrator,
        ISettingsStore settingsStore,
        IAutoStartManager autoStart,
        WindowSignalScreenShareDetector detector,
        Settings initialSettings,
        TrayNotifier? notifier = null)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _autoStart = autoStart;
        _detector = detector;
        _settings = initialSettings;
        _notifier = notifier;
    }

    public void Initialize()
    {
        StartupLog.Info("TrayIconHost.Initialize: entry");

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-icon.ico");
        StartupLog.Info($"TrayIconHost.Initialize: iconPath = {iconPath}");

        bool fileExists = File.Exists(iconPath);
        StartupLog.Info($"TrayIconHost.Initialize: File.Exists = {fileExists}");

        if (!fileExists)
        {
            var msg = $"Tray icon not found at: {iconPath}. Build did not copy Assets/tray-icon.ico to output directory.";
            Debug.WriteLine($"[Gausslite] ERROR: {msg}");
            StartupLog.Warn($"TrayIconHost.Initialize: {msg}");
            throw new FileNotFoundException(msg, iconPath);
        }

        StartupLog.Info("TrayIconHost.Initialize: creating BitmapImage...");
        var iconImage = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
        StartupLog.Info($"TrayIconHost.Initialize: BitmapImage created, IsFrozen={iconImage.IsFrozen}, PixelWidth={iconImage.PixelWidth}, PixelHeight={iconImage.PixelHeight}");

        StartupLog.Info("TrayIconHost.Initialize: creating TaskbarIcon...");
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Gausslite",
            // Left-click on the tray icon toggles blur on/off.  Same code path as the
            // tray menu's "Enable blur" entry and the global hotkey, so override balloons
            // and state machine behave identically across all three entry points.
            LeftClickCommand = new ToggleBlurCommand(_orchestrator),
        };
        _notifier?.Attach(_taskbarIcon);

        StartupLog.Info("TrayIconHost.Initialize: assigning IconSource...");
        _taskbarIcon.IconSource = iconImage;
        StartupLog.Info("TrayIconHost.Initialize: TaskbarIcon created and configured");
        StartupLog.Info($"TrayIconHost.Initialize: TaskbarIcon.Visibility = {_taskbarIcon.Visibility}");

        _taskbarIcon.Visibility = Visibility.Visible;
        StartupLog.Info("TrayIconHost.Initialize: Visibility forced to Visible");

        // Fallback: assign a native HICON via System.Drawing.Icon, which bypasses the
        // WPF imaging path entirely. H.NotifyIcon can consume either; if BitmapImage
        // conversion silently fails, this path may succeed.
        try
        {
            _taskbarIcon.Icon = new System.Drawing.Icon(iconPath);
            StartupLog.Info("TrayIconHost.Initialize: System.Drawing.Icon fallback set successfully");
        }
        catch (Exception ex)
        {
            StartupLog.Warn($"TrayIconHost.Initialize: System.Drawing.Icon fallback failed: {ex.Message}", ex);
        }

        _toggleItem = new MenuItem
        {
            Header = "Enable blur",
            IsCheckable = true,
            IsChecked = _orchestrator.IsBlurEnabled
        };
        _toggleItem.Click += (_, _) => _orchestrator.ToggleBlur();

        var intensitySubmenu = new MenuItem { Header = "Blur intensity" };
        _intensityLightItem  = CreateIntensityItem("Light",  BlurIntensityPreset.Light);
        _intensityMediumItem = CreateIntensityItem("Medium", BlurIntensityPreset.Medium);
        _intensityHeavyItem  = CreateIntensityItem("Heavy",  BlurIntensityPreset.Heavy);
        intensitySubmenu.Items.Add(_intensityLightItem);
        intensitySubmenu.Items.Add(_intensityMediumItem);
        intensitySubmenu.Items.Add(_intensityHeavyItem);
        UpdateIntensityCheckmarks(_orchestrator.CurrentIntensity);

        var scopeSubmenu = new MenuItem { Header = "Blur region" };
        _scopeChatListItem     = CreateScopeItem("Chat list",    BlurRegionScope.ChatList);
        _scopeConversationItem = CreateScopeItem("Conversation", BlurRegionScope.Conversation);
        _scopeBothItem         = CreateScopeItem("Both",         BlurRegionScope.Both);
        scopeSubmenu.Items.Add(_scopeChatListItem);
        scopeSubmenu.Items.Add(_scopeConversationItem);
        scopeSubmenu.Items.Add(_scopeBothItem);
        UpdateScopeCheckmarks(_orchestrator.CurrentScope);

        // ── Persistent toggles (v0.3.5) ──────────────────────────────────────
        _autoStartItem = new MenuItem
        {
            Header = "Auto-start with Windows",
            IsCheckable = true,
            IsChecked = _settings.AutoStart,
        };
        _autoStartItem.Click += (_, _) => OnAutoStartToggled();

        _processHeuristicItem = new MenuItem
        {
            Header = "Blur on any sharing app",
            IsCheckable = true,
            IsChecked = _settings.ProcessRunningHeuristicEnabled,
            ToolTip = "Treats any running Zoom / Teams / Discord desktop as an active share. " +
                      "Catches Discord desktop sharing (which is invisible to window detection); " +
                      "downside: blur turns on whenever those apps are running.",
        };
        _processHeuristicItem.Click += (_, _) => OnProcessHeuristicToggled();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(intensitySubmenu);
        menu.Items.Add(scopeSubmenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(_processHeuristicItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        _taskbarIcon.ContextMenu = menu;

        _orchestrator.BlurStateChanged += OnBlurStateChanged;
        StartupLog.Info("TrayIconHost.Initialize: exit");
    }

    private void OnBlurStateChanged(object? sender, bool isEnabled)
    {
        if (_toggleItem is not null)
            _toggleItem.IsChecked = isEnabled;
    }

    private MenuItem CreateIntensityItem(string header, BlurIntensityPreset preset)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        item.Click += (_, _) =>
        {
            _orchestrator.SetIntensity(preset);
            UpdateIntensityCheckmarks(preset);
            PersistSettings(_settings with { Intensity = preset });
        };
        return item;
    }

    private void UpdateIntensityCheckmarks(BlurIntensityPreset selected)
    {
        if (_intensityLightItem is not null)  _intensityLightItem.IsChecked  = selected == BlurIntensityPreset.Light;
        if (_intensityMediumItem is not null) _intensityMediumItem.IsChecked = selected == BlurIntensityPreset.Medium;
        if (_intensityHeavyItem is not null)  _intensityHeavyItem.IsChecked  = selected == BlurIntensityPreset.Heavy;
    }

    private MenuItem CreateScopeItem(string header, BlurRegionScope scope)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        item.Click += (_, _) =>
        {
            _orchestrator.SetScope(scope);
            UpdateScopeCheckmarks(scope);
            PersistSettings(_settings with { Scope = scope });
        };
        return item;
    }

    // ── v0.3.5 toggle handlers + persistence helper ─────────────────────────

    private void OnAutoStartToggled()
    {
        // The checkable WPF item flips IsChecked before firing Click; read the new state.
        bool desired = _autoStartItem!.IsChecked;
        bool ok = desired ? _autoStart.Enable() : _autoStart.Disable();
        if (!ok)
        {
            // Revert the visual toggle if the registry write failed.
            _autoStartItem.IsChecked = !desired;
            return;
        }
        PersistSettings(_settings with { AutoStart = desired });
    }

    private void OnProcessHeuristicToggled()
    {
        bool desired = _processHeuristicItem!.IsChecked;
        _detector.SetTriggerProcessNames(
            desired
                ? WindowSignalScreenShareDetector.DefaultTriggerProcessNames
                : Array.Empty<string>());
        PersistSettings(_settings with { ProcessRunningHeuristicEnabled = desired });
    }

    private void PersistSettings(Settings updated)
    {
        _settings = updated;
        _settingsStore.Save(updated);
    }

    private void UpdateScopeCheckmarks(BlurRegionScope selected)
    {
        if (_scopeChatListItem is not null)     _scopeChatListItem.IsChecked     = selected == BlurRegionScope.ChatList;
        if (_scopeConversationItem is not null) _scopeConversationItem.IsChecked = selected == BlurRegionScope.Conversation;
        if (_scopeBothItem is not null)         _scopeBothItem.IsChecked         = selected == BlurRegionScope.Both;
    }

    public void Dispose()
    {
        _orchestrator.BlurStateChanged -= OnBlurStateChanged;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }

    // Bridges TaskbarIcon.LeftClickCommand → ITrayOrchestrator.ToggleBlur. Always enabled.
    private sealed class ToggleBlurCommand : ICommand
    {
        private readonly ITrayOrchestrator _orchestrator;
        public ToggleBlurCommand(ITrayOrchestrator orchestrator) => _orchestrator = orchestrator;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _orchestrator.ToggleBlur();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
