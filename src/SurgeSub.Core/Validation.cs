using System.Text.RegularExpressions;

namespace SurgeSub.Core;

public static partial class Validation
{
    public const string MissingDevice = "ssunavail0";
    public static Uri Endpoint(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ClientException("请输入 HTTPS 订阅地址（本机测试可用 HTTP）。");
        var b = new UriBuilder(uri);
        var path = b.Path.TrimEnd('/');
        if (path.EndsWith("/surgemac/combined", StringComparison.Ordinal))
            b.Path = path[..^"combined".Length] + "client-bundle";
        else if (path.EndsWith("/surgeios/combined", StringComparison.Ordinal))
            b.Path = path[..^"surgeios/combined".Length] + "surgemac/client-bundle";
        else if (!path.EndsWith("/surgemac/client-bundle", StringComparison.Ordinal))
            throw new ClientException("地址须为 SubVault 的订阅地址或 /surgemac/client-bundle 接口。");
        return b.Uri;
    }

    public static void Settings(ClientSettings s)
    {
        _ = Endpoint(s.SubscriptionUrl);
        if (s.Home is null || s.Other is null) throw new ClientException("必须手动选择家宽和非家宽网卡。");
        if (s.Home.ServiceId == s.Other.ServiceId) throw new ClientException("家宽和非家宽必须选择不同网卡。");
        if (s.ProxyPort is not (>= 1 and <= 65535)) throw new ClientException("必须填写有效的本地 SOCKS5 端口（1–65535）。");
        ProcessPath(s.ProxyApplication);
    }

    public static string ProcessPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.IndexOfAny(['\r', '\n', ',', '#', ';', '=', '"', '*', '?']) >= 0
            || path.Contains("//", StringComparison.Ordinal))
            throw new ClientException("必须选择代理 App 或实际可执行文件的完整路径。");
        var full = Path.GetFullPath(path).TrimEnd('/');
        if (full.Contains("/Surge.app", StringComparison.OrdinalIgnoreCase) || full == "/" || full.Length < 5)
            throw new ClientException("代理应用不能选择 Surge 自身或根目录。");
        return full.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? full + "/" : full;
    }

    public static AdapterInfo? Resolve(AdapterChoice choice, IReadOnlyList<AdapterInfo> adapters)
    {
        var exact = adapters.Where(a => a.ServiceId == choice.ServiceId && a.Hardware == choice.Hardware).ToArray();
        if (exact.Length == 1) return exact[0];
        var identity = adapters.Where(a => a.Hardware == choice.Hardware && choice.MacAddress.Length > 0 && choice.MacAddress != "020000000000"
            && a.MacAddress.Equals(choice.MacAddress, StringComparison.OrdinalIgnoreCase)).ToArray();
        return identity.Length == 1 ? identity[0] : null;
    }

    public static string Device(AdapterInfo? adapter) => adapter is { Available: true } && DevicePattern().IsMatch(adapter.Device)
        ? adapter.Device : MissingDevice;
    public static void Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.IndexOfAny(['\r', '\n', ',', '=', '[', ']', '#', ';', '"']) >= 0)
            throw new ClientException("服务器返回了无效的策略名称。");
    }
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,14}$")]
    private static partial Regex DevicePattern();
}
