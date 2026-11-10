using System.Text.Json;
using SurgeSub.Core;

if (!OperatingSystem.IsMacOS()) { Console.Error.WriteLine("SurgeSub Agent requires macOS."); return 1; }
var storage = new Storage(Storage.DefaultRoot);
var commands = new CommandRunner();
var mac = new MacSystem(commands);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;
try
{
    if (args.Contains("--adapters"))
    {
        Console.WriteLine(JsonSerializer.Serialize(await mac.Adapters(ct), ClientJson.Default.AdapterInfoArray)); return 0;
    }
    if (args is ["--verify-proxy", var app, var port] && int.TryParse(port, out var value))
    {
        var adapters = await mac.Adapters(ct);
        var a = adapters.FirstOrDefault() ?? throw new ClientException("没有可用网卡。");
        var b = adapters.FirstOrDefault(x => x.ServiceId != a.ServiceId) ?? throw new ClientException("需要两张网卡。");
        await mac.VerifyProxy(new("https://example.test/surgemac/client-bundle", a.Choice, b.Choice, value, app), ct);
        Console.WriteLine("所选应用拥有该端口，SOCKS5 握手成功。"); return 0;
    }
    if (!args.Contains("--once") && !args.Contains("--watch"))
    {
        Console.WriteLine("Usage: SurgeSub.Agent --once | --watch | --adapters | --verify-proxy <app-path> <port>"); return 0;
    }
    do
    {
        try
        {
            var settings = storage.Settings();
            if (args.Contains("--once") || settings?.AutoUpdate == true)
            {
                if (settings is null) throw new ClientException("请先完成订阅、网卡、端口和代理应用设置。");
                Validation.Settings(settings);
                // Bootstrap through the explicitly selected proxy, not an old/broken Surge interface binding.
                var sync = new SyncService(storage, mac, current => new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false,
                    Proxy = new System.Net.WebProxy($"socks5://127.0.0.1:{current.ProxyPort}"), UseProxy = true })
                    { Timeout = TimeSpan.FromSeconds(45) }, new ProfilePublisher(storage, new SurgeController(commands), SurgeController.ProfilePath));
                var status = await sync.Run(args.Contains("--once"), ct);
                if (args.Contains("--once")) { Console.WriteLine(status.Message); return status.Success ? 0 : 2; }
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            var message = Storage.Error(e);
            var old = storage.Read(storage.StatusPath, ClientJson.Default.SyncStatus);
            if (old?.Message != message) storage.Log(message);
            storage.Save(storage.StatusPath, new SyncStatus(DateTimeOffset.Now, false, message), ClientJson.Default.SyncStatus);
            if (args.Contains("--once")) { Console.Error.WriteLine(message); return 1; }
        }
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
    } while (!ct.IsCancellationRequested);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
catch (Exception e) { Console.Error.WriteLine(Storage.Error(e)); return 1; }
return 0;
