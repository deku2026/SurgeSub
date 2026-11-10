using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using SurgeSub.Core;

namespace SurgeSub.App;

internal sealed class MenuBarController
{
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly MainViewModel model = new();
    private readonly LoginService login = new(new CommandRunner());
    private readonly TrayIcon tray;
    private readonly NativeMenuItem statusItem, syncItem, quitItem;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private MainWindow? window;
    private bool quitting, refreshing;

    public MenuBarController(Application app, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        desktop = lifetime;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        tray = TrayIcon.GetIcons(app)!.Single();
        var menu = tray.Menu!;
        statusItem = menu.Items.OfType<NativeMenuItem>().First();
        syncItem = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "立即同步");
        quitItem = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "退出 SurgeSub");
        menu.NeedsUpdate += (_, _) => { UpdateCommands(); _ = RefreshStatus(); };
        model.PropertyChanged += (_, _) => UpdateCommands();
        timer.Tick += async (_, _) => await RefreshStatus();
        desktop.Exit += (_, _) => timer.Stop();
        if (app.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) ShowSettings(); };
        // Keep MainWindow null during desktop startup so a configured app starts in the menu bar.
        Dispatcher.UIThread.Post(async () =>
        {
            await model.Load();
            if (!model.HasSavedSettings && !(desktop.Args?.Contains("--background") ?? false)) ShowSettings();
            await RefreshStatus();
            timer.Start();
        });
    }

    public void ShowSettings()
    {
        if (quitting) return;
        if (window is null)
        {
            window = new MainWindow { DataContext = model };
            window.Closing += (_, e) =>
            {
                if (quitting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
                e.Cancel = true;
                window.Hide();
            };
            window.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
                { e.Handled = true; await Quit(); }
            };
            desktop.MainWindow = window;
        }
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public async Task SyncNow()
    {
        if (model.Busy || quitting) return;
        if (!model.HasSavedSettings) { ShowSettings(); return; }
        await model.SyncSavedSettings();
        await RefreshStatus();
    }

    public async Task Quit()
    {
        if (model.Busy || quitting) return;
        quitting = true;
        UpdateCommands();
        tray.ToolTipText = "SurgeSub：正在停止自动同步…";
        try
        {
            await login.Stop();
            timer.Stop();
            tray.IsVisible = false;
            desktop.Shutdown();
        }
        catch (Exception e)
        {
            quitting = false;
            model.Status = Storage.Error(e);
            ShowSettings();
            UpdateCommands();
        }
    }

    private void UpdateCommands()
    {
        syncItem.IsEnabled = !model.Busy && !quitting && model.HasSavedSettings;
        quitItem.IsEnabled = !model.Busy && !quitting;
    }

    private async Task RefreshStatus()
    {
        if (refreshing || quitting) return;
        refreshing = true;
        try
        {
            var active = await login.IsRunning();
            statusItem.Header = model.Busy ? "正在操作…" : !model.HasSavedSettings ? "尚未配置"
                : active ? "自动同步：运行中" : model.SavedAutoUpdate ? "自动同步：已停止" : "自动同步：关闭";
            tray.ToolTipText = "SurgeSub · " + statusItem.Header;
            UpdateCommands();
        }
        catch (Exception) { statusItem.Header = "后台状态暂不可用"; }
        finally { refreshing = false; }
    }
}
