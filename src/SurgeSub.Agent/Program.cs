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
    using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
    var sync = new SyncService(storage, mac, new BundleClient(http), new ProfilePublisher(storage, new SurgeController(commands), SurgeController.ProfilePath));
    do
    {
        try
        {
            var settings = storage.Settings();
            if (args.Contains("--once") || settings?.AutoUpdate == true)
            {
                var status = await sync.Run(args.Contains("--once"), ct);
                if (args.Contains("--once")) { Console.WriteLine(status.Message); return status.Success ? 0 : 2; }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
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
catch (OperationCanceledException) { }
catch (Exception e) { Console.Error.WriteLine(Storage.Error(e)); return 1; }
return 0;
