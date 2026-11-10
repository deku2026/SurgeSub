using System.Text.Json;

namespace SurgeSub.Core;

public interface ISurgeController
{
    Task Validate(string path, CancellationToken ct);
    Task<string> Current(CancellationToken ct);
    Task Apply(string path, GeneratedProfile expected, bool activate, CancellationToken ct);
}

public sealed class SurgeController(ICommandRunner runner) : ISurgeController
{
    public const string Cli = "/Applications/Surge.app/Contents/Applications/surge-cli";
    public const string ProfileName = "SurgeSub";
    public static string ProfilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Surge/Profiles/SurgeSub.conf");
    private async Task<string> Run(string[] args, CancellationToken ct)
    {
        var result = await runner.Run(Cli, args, ct, 45);
        if (result.ExitCode != 0) throw new ClientException("Surge 操作失败（" + args[0] + "）；请检查 Surge 是否运行及其配置错误提示。");
        return result.Output;
    }
    public async Task Validate(string path, CancellationToken ct) => _ = await Run(["--check", path], ct);
    public async Task<string> Current(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Run(["--raw", "profile", "current"], ct));
        return doc.RootElement.GetProperty("profile").GetString()!;
    }
    public async Task Apply(string path, GeneratedProfile expected, bool activate, CancellationToken ct)
    {
        var current = await Current(ct);
        if (!activate && current != ProfileName) return; // Background updates never switch away from a user's other profile.
        var selections = await Selections(ct);
        await Run(current == ProfileName ? ["reload"] : ["profile", "switch", ProfileName], ct);
        // reload returns before the engine has finished applying changes.
        var applied = false;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(300, ct);
            using var dump = JsonDocument.Parse(await Run(["--raw", "dump", "profile"], ct));
            var profile = dump.RootElement.GetProperty("profile").GetString() ?? "";
            if (BindingsMatch(profile, expected.Content)
                && profile.Contains(expected.Fingerprint, StringComparison.Ordinal)
                && await Current(ct) == ProfileName && await ResourcesReady(expected, ct)) { applied = true; break; }
        }
        if (!applied) throw new ClientException("Surge 尚未应用新配置，将恢复上一版本。");
        using var groups = JsonDocument.Parse(await Run(["--raw", "policy-group", "list"], ct));
        foreach (var group in groups.RootElement.GetProperty("groups").EnumerateArray())
        {
            var name = group.GetProperty("name").GetString()!;
            if (!selections.TryGetValue(name, out var choice)) continue;
            if (choice.StartsWith("非家宽 SOCKS5 · ", StringComparison.Ordinal)) choice = "本地代理 · 非家宽";
            if (group.GetProperty("options").EnumerateArray().Any(p => p.GetProperty("name").GetString() == choice))
                await Run(["policy-group", "set", name, choice], ct);
        }
    }
    public static bool BindingsMatch(string runtime, string candidate)
    {
        var pattern = @"(?:^|,)\s*interface\s*=\s*([^,\s]+)";
        foreach (var line in candidate.Split('\n').Where(l => l.Contains(" = direct,")))
        {
            var (name, value) = ProfileGenerator.Assignment(line);
            var expected = System.Text.RegularExpressions.Regex.Match(value, pattern);
            if (!expected.Success) continue;
            var found = runtime.Split('\n').FirstOrDefault(l => l.StartsWith(name + " = ", StringComparison.Ordinal));
            if (found is null || System.Text.RegularExpressions.Regex.Match(found, pattern).Groups[1].Value != expected.Groups[1].Value) return false;
        }
        return true;
    }
    private async Task<bool> ResourcesReady(GeneratedProfile expected, CancellationToken ct)
    {
        using var resources = JsonDocument.Parse(await Run(["--raw", "external-resource", "list"], ct));
        var local = resources.RootElement.GetProperty("defines").EnumerateArray()
            .Where(r => r.GetProperty("type").GetString() == "policy-group" && (r.GetProperty("path").GetString() ?? "").Contains(expected.Fingerprint, StringComparison.Ordinal)).ToArray();
        var referenced = System.Text.RegularExpressions.Regex.Matches(expected.Content, "policy-path=\"[^\"]+\"").Count;
        return local.Length == referenced && local.All(r => r.TryGetProperty("ready", out var ready) && ready.GetBoolean() && !r.TryGetProperty("error", out _));
    }
    private async Task<Dictionary<string, string>> Selections(CancellationToken ct)
    {
        using var groups = JsonDocument.Parse(await Run(["--raw", "policy-group", "list"], ct));
        return groups.RootElement.GetProperty("groups").EnumerateArray()
            .Where(g => g.GetProperty("type").GetString() == "Manual Selection Group" && g.TryGetProperty("selected", out _))
            .ToDictionary(g => g.GetProperty("name").GetString()!, g => g.GetProperty("selected").GetString()!);
    }
    public async Task Restore(string previous, CancellationToken ct) => _ = await Run(
        await Current(ct) == previous ? ["reload"] : ["profile", "switch", previous], ct);
}
