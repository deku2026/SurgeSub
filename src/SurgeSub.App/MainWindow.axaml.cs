using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SurgeSub.Core;

namespace SurgeSub.App;
public partial class MainWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly CancellationTokenSource pickerLifetime = new();
    private MainViewModel Model => (MainViewModel)DataContext!;
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => { await Model.Load(); timer.Start(); };
        timer.Tick += (_, _) => Model.ReadStatus();
        Closed += (_, _) => { timer.Stop(); pickerLifetime.Cancel(); pickerLifetime.Dispose(); };
    }
    private async void RefreshClick(object? sender, RoutedEventArgs e) => await Model.Refresh();
    private async void ApplyClick(object? sender, RoutedEventArgs e) => await Model.Apply();
    private async void LogsClick(object? sender, RoutedEventArgs e)
    { try { await Model.OpenLogs(); } catch (Exception error) { Model.Status = Storage.Error(error); } }
    private async void BrowseClick(object? sender, RoutedEventArgs e) => await Model.BrowseApplication(pickerLifetime.Token);
}
