using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Gausslite.App.Diagnostics;
using Gausslite.App.Orchestration;
using Gausslite.Core.Blur;

namespace Gausslite.App.Tray;

/// <summary>
/// Owns the system-tray <see cref="TaskbarIcon"/> and keeps its "Enable blur" checked
/// state in sync with <see cref="ITrayOrchestrator.IsBlurEnabled"/>.
/// Must be created and disposed on the WPF UI thread.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly ITrayOrchestrator _orchestrator;
    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _toggleItem;
    private MenuItem? _intensityLightItem;
    private MenuItem? _intensityMediumItem;
    private MenuItem? _intensityHeavyItem;

    public TrayIconHost(ITrayOrchestrator orchestrator) => _orchestrator = orchestrator;

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
        };

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

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(intensitySubmenu);
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
        };
        return item;
    }

    private void UpdateIntensityCheckmarks(BlurIntensityPreset selected)
    {
        if (_intensityLightItem is not null)  _intensityLightItem.IsChecked  = selected == BlurIntensityPreset.Light;
        if (_intensityMediumItem is not null) _intensityMediumItem.IsChecked = selected == BlurIntensityPreset.Medium;
        if (_intensityHeavyItem is not null)  _intensityHeavyItem.IsChecked  = selected == BlurIntensityPreset.Heavy;
    }

    public void Dispose()
    {
        _orchestrator.BlurStateChanged -= OnBlurStateChanged;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }
}
