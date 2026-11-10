using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace SurgeSub.Core;

public sealed class MacSystem(ICommandRunner commands)
{
    public async Task<AdapterInfo[]> Adapters(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS()) throw new ClientException("网卡识别仅支持 macOS。");
        var result = await commands.Run("/usr/bin/plutil", ["-convert", "json", "-o", "-", "/Library/Preferences/SystemConfiguration/preferences.plist"], ct);
        if (result.ExitCode != 0) throw new ClientException("无法读取 macOS 网络服务列表。");
        using var doc = JsonDocument.Parse(result.Output);
        var devices = NetworkInterface.GetAllNetworkInterfaces().GroupBy(n => n.Name).ToDictionary(g => g.Key, g => g.First());
        var list = new List<AdapterInfo>();
        foreach (var service in doc.RootElement.GetProperty("NetworkServices").EnumerateObject())
        {
            if (!service.Value.TryGetProperty("Interface", out var iface) || !iface.TryGetProperty("DeviceName", out var deviceElement)) continue;
            var device = deviceElement.GetString()!;
            if (!devices.TryGetValue(device, out var network) || !device.StartsWith("en", StringComparison.Ordinal)) continue;
            var hardware = iface.TryGetProperty("UserDefinedName", out var label) ? label.GetString() ?? "" : device;
            var name = service.Value.TryGetProperty("UserDefinedName", out label) ? label.GetString() ?? hardware : hardware;
            var addresses = network.GetIPProperties().UnicastAddresses.Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)
                    && !(a.GetAddressBytes()[0] == 169 && a.GetAddressBytes()[1] == 254)).Select(a => a.ToString()).ToArray();
            var available = network.OperationalStatus == OperationalStatus.Up && addresses.Length > 0;
            if (available)
            {
                var route = await commands.Run("/sbin/route", ["-n", "get", "-ifscope", device, "1.1.1.1"], ct, 5);
                available = route.ExitCode == 0 && route.Output.Contains("interface: " + device, StringComparison.Ordinal);
            }
            list.Add(new(service.Name, name, hardware, device, network.GetPhysicalAddress().ToString(), available, addresses));
        }
        return list.DistinctBy(a => a.Device).OrderByDescending(a => a.Available).ThenBy(a => a.Name).ToArray();
    }

    public async Task VerifyProxy(ClientSettings settings, CancellationToken ct = default)
    {
        Validation.Settings(settings);
        var selected = Validation.ProcessPath(settings.ProxyApplication);
        if (!File.Exists(selected) && !Directory.Exists(selected)) throw new ClientException("所选代理应用已不存在，请重新选择。");
        var listeners = await commands.Run("/usr/sbin/lsof", ["-nP", $"-iTCP:{settings.ProxyPort}", "-sTCP:LISTEN", "-Fp"], ct);
        var pids = listeners.Output.Split('\n').Where(l => l.StartsWith('p')).Select(l => l[1..]).Distinct().ToArray();
        if (pids.Length == 0) throw new ClientException("代理端口没有监听，请先启动所选代理应用。");
        foreach (var pid in pids)
        {
            if (!int.TryParse(pid, out _)) throw new ClientException("无法识别端口所属进程。");
            var owner = (await commands.Run("/bin/ps", ["-p", pid, "-o", "comm="], ct)).Output.Trim();
            if (!OwnsProcess(selected, owner)) throw new ClientException("端口监听进程不属于所选应用；若核心位于 App 外，请选择实际核心可执行文件。");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, settings.ProxyPort!.Value, deadline.Token);
        var stream = tcp.GetStream();
        await stream.WriteAsync(new byte[] { 5, 1, 0 }, deadline.Token);
        var response = new byte[2]; await stream.ReadExactlyAsync(response, deadline.Token);
        if (response[0] != 5 || response[1] != 0) throw new ClientException("所选端口不支持免认证 SOCKS5，请检查代理软件设置。");
    }

    public static bool OwnsProcess(string selected, string owner) => selected.EndsWith('/')
        ? owner.StartsWith(selected, StringComparison.Ordinal) : owner.Equals(selected, StringComparison.Ordinal);
}
