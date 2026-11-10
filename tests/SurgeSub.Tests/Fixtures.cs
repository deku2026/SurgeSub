using SurgeSub.Core;

namespace SurgeSub.Tests;
internal static class Fixtures
{
    public static AdapterInfo Home = new("home-service", "iPhone USB", "iPhone USB", "en10", "AABB", true, ["172.20.10.3"]);
    public static AdapterInfo Other = new("wifi-service", "Wi-Fi", "Wi-Fi", "en1", "CCDD", true, ["192.168.1.2"]);
    public static AdapterInfo[] Adapters => [Home, Other];
    public static ClientSettings Settings => new("https://server.test/surgemac/combined?token=test", Home.Choice, Other.Choice, 7890, "/Applications/FlClash.app");
    public static ClientBundle Bundle => new(1, "test-revision", 1200, new("家宽网卡直连", "非家宽网卡直连", "本地代理 · 非家宽"),
    [
        new("General", ["loglevel = notify", "dns-server = 1.1.1.1"]),
        new("Proxy", ["家宽网卡直连 = direct, dns-follow-interface=false, test-url=https://www.gstatic.com/generate_204, test-timeout=8", "非家宽网卡直连 = direct, dns-follow-interface=false"]),
        new("Proxy Group", ["Global = select, 家宽网卡直连, 非家宽网卡直连, 本地代理 · 非家宽, policy-path=surgesub-resource:all.conf, update-interval=1200"]),
        new("Rule", ["PROTOCOL,DOH,Global", "PROCESS-NAME,/Applications/Firefox.app/,Global", "FINAL,Global,dns-failed"]),
        new("Host", [])
    ], [new("all.conf", ["Node A = ss, 192.0.2.1, 443, encrypt-method=aes-128-gcm, password=fixture-only, udp-relay=true"]) ]);
}
