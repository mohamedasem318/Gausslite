using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using WAshed.App.Orchestration;

namespace WAshed.App.Tray;

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

    public TrayIconHost(ITrayOrchestrator orchestrator) => _orchestrator = orchestrator;

    public void Initialize()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-icon.ico");
        if (!File.Exists(iconPath))
        {
            var msg = $"Tray icon not found at: {iconPath}. Build did not copy Assets/tray-icon.ico to output directory.";
            Debug.WriteLine($"[WAshed] ERROR: {msg}");
            throw new FileNotFoundException(msg, iconPath);
        }

        var iconImage = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "WAshed",
            IconSource = iconImage
        };

        _toggleItem = new MenuItem
        {
            Header = "Enable blur",
            IsCheckable = true,
            IsChecked = _orchestrator.IsBlurEnabled
        };
        _toggleItem.Click += (_, _) => _orchestrator.ToggleBlur();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        _taskbarIcon.ContextMenu = menu;

        _orchestrator.BlurStateChanged += OnBlurStateChanged;
    }

    private void OnBlurStateChanged(object? sender, bool isEnabled)
    {
        if (_toggleItem is not null)
            _toggleItem.IsChecked = isEnabled;
    }

    public void Dispose()
    {
        _orchestrator.BlurStateChanged -= OnBlurStateChanged;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }
}
