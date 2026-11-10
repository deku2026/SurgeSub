using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SurgeSub.App;
public partial class App : Application
{
    private MenuBarController? menuBar;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            menuBar = new MenuBarController(this, desktop);
        base.OnFrameworkInitializationCompleted();
    }
    private void OpenSettings(object? sender, EventArgs e) => menuBar?.ShowSettings();
    private async void SyncNow(object? sender, EventArgs e) { if (menuBar is not null) await menuBar.SyncNow(); }
    private async void Quit(object? sender, EventArgs e) { if (menuBar is not null) await menuBar.Quit(); }
}
