using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SurgeSub.Core;

namespace SurgeSub.App;
public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly Storage storage = new(Storage.DefaultRoot);
    private readonly CommandRunner commands = new();
    public ObservableCollection<AdapterInfo> Adapters { get; } = [];
    private AdapterInfo[] allAdapters = [];
    private AdapterChoice? preferredHome, preferredOther;
    private string subscriptionUrl = "", proxyPort = "", proxyApplication = "", status = "选择出口和代理应用后，生成本机专用配置。";
    private AdapterInfo? home, other;
    private bool busy, autoUpdate = true, startAtLogin, showDisconnectedAdapters;
    private ClientSettings? loaded;
    public string SubscriptionUrl { get => subscriptionUrl; set => Set(ref subscriptionUrl, value); }
    public string ProxyPort { get => proxyPort; set => Set(ref proxyPort, value); }
    public string ProxyApplication { get => proxyApplication; set => Set(ref proxyApplication, value); }
    public AdapterInfo? Home { get => home; set { if (value is not null) preferredHome = value.Choice; Set(ref home, value); } }
    public AdapterInfo? Other { get => other; set { if (value is not null) preferredOther = value.Choice; Set(ref other, value); } }
    public bool ShowDisconnectedAdapters
    {
        get => showDisconnectedAdapters;
        set { Set(ref showDisconnectedAdapters, value); FilterAdapters(); }
    }
    public string Status { get => status; set => Set(ref status, value); }
    public bool Busy { get => busy; set { Set(ref busy, value); Changed(nameof(Idle)); } }
    public bool Idle => !Busy;
    public bool AutoUpdate { get => autoUpdate; set => Set(ref autoUpdate, value); }
    public bool StartAtLogin { get => startAtLogin; set => Set(ref startAtLogin, value); }
    public bool CanApply
    {
        get
        {
            if (Busy) return false;
            try { Validation.Settings(Settings()); return true; } catch { return false; }
        }
    }
    private ClientSettings Settings() => new(SubscriptionUrl.Trim(), Home?.Choice, Other?.Choice,
        int.TryParse(ProxyPort, out var port) ? port : null, ProxyApplication.Trim(), AutoUpdate, StartAtLogin);
    public async Task Load()
    {
        try
        {
            loaded = storage.Settings();
            if (loaded is not null)
            {
                SubscriptionUrl = loaded.SubscriptionUrl; ProxyPort = loaded.ProxyPort?.ToString() ?? "";
                ProxyApplication = loaded.ProxyApplication; AutoUpdate = loaded.AutoUpdate; StartAtLogin = loaded.StartAtLogin;
            }
            await Refresh(); ReadStatus();
        }
        catch (Exception e) { Status = Storage.Error(e); }
    }
    public async Task Refresh()
    {
        Busy = true;
        try
        {
            allAdapters = await new MacSystem(commands).Adapters();
            FilterAdapters();
            Status = "网卡列表已刷新，默认仅显示已连接且有可用出口的网卡。";
        }
        catch (Exception e) { Status = Storage.Error(e); }
        finally { Busy = false; }
    }
    private void FilterAdapters()
    {
        var homeChoice = preferredHome ?? loaded?.Home; var otherChoice = preferredOther ?? loaded?.Other;
        var visible = allAdapters.Where(a => ShowDisconnectedAdapters || a.Available).ToArray();
        Adapters.Clear(); foreach (var a in visible) Adapters.Add(a);
        Home = homeChoice is null ? null : Validation.Resolve(homeChoice, visible);
        Other = otherChoice is null ? null : Validation.Resolve(otherChoice, visible);
    }
    public async Task Apply()
    {
        Busy = true;
        try
        {
            var settings = Settings(); Validation.Settings(settings);
            Status = "验证端口和代理进程…";
            await new MacSystem(commands).VerifyProxy(settings);
            var agent = Path.Combine(AppContext.BaseDirectory, "SurgeSub.Agent");
            if (!File.Exists(agent)) throw new ClientException("后台组件缺失，请通过打包脚本构建完整应用。");
            storage.Save(storage.SettingsPath, settings, ClientJson.Default.ClientSettings);
            loaded = settings;
            Status = "获取订阅、生成并校验配置…";
            var result = await commands.Run(agent, ["--once"], timeoutSeconds: 180);
            ReadStatus(true);
            if (result.ExitCode is 0 or 2)
            {
                await new LoginService(commands).Configure(StartAtLogin, agent, CancellationToken.None, AutoUpdate);
                Status += !AutoUpdate ? " 自动同步已关闭。" : StartAtLogin ? " 登录后台同步已启用。" : " 本次登录期间后台同步已启用。";
            }
        }
        catch (Exception e) { Status = Storage.Error(e); storage.Log(Status); }
        finally { Busy = false; }
    }
    public void ReadStatus(bool force = false)
    {
        if (Busy && !force) return;
        try
        {
            if (storage.Read(storage.StatusPath, ClientJson.Default.SyncStatus) is { } s)
                Status = $"{s.At.ToLocalTime():MM-dd HH:mm:ss} · {s.Message}";
        }
        catch (Exception e) { Status = Storage.Error(e); }
    }
    public async Task OpenLogs()
    {
        Storage.PrivateDirectory(storage.Root);
        await commands.Run("/usr/bin/open", [storage.Root]);
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Changed(name); Changed(nameof(CanApply)); }
    private void Changed(string? name) => PropertyChanged?.Invoke(this, new(name));
}
