using System.Xml.Linq;

namespace SurgeSub.Core;

public sealed class LoginService(ICommandRunner commands, string? dataDirectory = null, string? launchAgentsDirectory = null)
{
    public const string Label = "com.arcforges.surgesub.agent";
    public const string MenuLabel = "com.arcforges.surgesub.menu";
    private string Root => dataDirectory ?? Storage.DefaultRoot;
    private string LaunchAgents => launchAgentsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/LaunchAgents");

    // Unload this session only. Persistent plists remain available for the next login.
    public async Task Stop(CancellationToken ct = default)
        => await StopJob(await UserTarget(ct), Label, ct);

    public async Task<bool> IsRunning(CancellationToken ct = default)
    {
        var state = await commands.Run("/bin/launchctl", ["print", await UserTarget(ct) + "/" + Label], ct);
        if (state.ExitCode == 113) return false;
        if (state.ExitCode != 0) throw new ClientException("无法检查后台任务状态，请稍后重试。");
        return true;
    }

    private async Task<string> UserTarget(CancellationToken ct)
    {
        var uid = (await commands.Run("/usr/bin/id", ["-u"], ct)).Output.Trim();
        if (!int.TryParse(uid, out _)) throw new ClientException("无法确定登录用户。");
        return "gui/" + uid;
    }

    private async Task StopJob(string target, string label, CancellationToken ct)
    {
        var job = target + "/" + label;
        var state = await commands.Run("/bin/launchctl", ["print", job], ct);
        if (state.ExitCode == 113) return; // launchctl: service not found.
        if (state.ExitCode != 0) throw new ClientException("无法检查后台任务状态，请稍后重试。");
        if ((await commands.Run("/bin/launchctl", ["bootout", job], ct, 90)).ExitCode != 0)
            throw new ClientException("后台任务未能停止，请稍后重试退出。");
    }

    public async Task Configure(bool startAtLogin, string agentPath, CancellationToken ct, bool autoUpdate = false)
    {
        var directory = LaunchAgents;
        Directory.CreateDirectory(directory);
        var persistent = Path.Combine(directory, Label + ".plist");
        var menuPath = Path.Combine(directory, MenuLabel + ".plist");
        var appPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(agentPath)!, "..", ".."));
        if ((startAtLogin || autoUpdate) && (!File.Exists(agentPath)
            || !File.Exists(Path.Combine(appPath, "Contents/MacOS/SurgeSub.App"))))
            throw new ClientException("找不到完整的应用程序，请使用 SurgeSub.app 安装包。");
        var target = await UserTarget(ct);
        await StopJob(target, Label, ct);
        await StopJob(target, MenuLabel, ct);
        if (File.Exists(persistent)) File.Delete(persistent);
        if (File.Exists(menuPath)) File.Delete(menuPath);
        if (startAtLogin)
        {
            // LaunchServices reuses the running app. Do not KeepAlive the GUI: Quit must stick.
            Storage.AtomicWrite(menuPath, Plist(
                new XElement("key", "Label"), new XElement("string", MenuLabel),
                new XElement("key", "ProgramArguments"), new XElement("array",
                    new XElement("string", "/usr/bin/open"), new XElement("string", "-g"),
                    new XElement("string", appPath), new XElement("string", "--args"), new XElement("string", "--background")),
                new XElement("key", "RunAtLoad"), new XElement("true")));
        }
        if (!autoUpdate) return;
        var root = Root;
        Storage.PrivateDirectory(root);
        // A session-only launchd job keeps syncing after the window closes without enabling login startup.
        var path = startAtLogin ? persistent : Path.Combine(root, "session-agent.plist");
        var plist = Plist(
                new XElement("key", "Label"), new XElement("string", Label),
                new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", agentPath), new XElement("string", "--watch")),
                new XElement("key", "RunAtLoad"), new XElement("true"),
                new XElement("key", "KeepAlive"), new XElement("true"),
                new XElement("key", "ExitTimeOut"), new XElement("integer", "60"),
                new XElement("key", "ThrottleInterval"), new XElement("integer", "30"),
                new XElement("key", "ProcessType"), new XElement("string", "Background"),
                new XElement("key", "StandardOutPath"), new XElement("string", Path.Combine(root, "agent-stdout.log")),
                new XElement("key", "StandardErrorPath"), new XElement("string", Path.Combine(root, "agent-stderr.log")));
        Storage.AtomicWrite(path, plist);
        if ((await commands.Run("/bin/launchctl", ["bootstrap", target, path], ct)).ExitCode != 0)
            throw new ClientException("登录启动注册失败，请查看系统设置中的后台项目。");
    }

    private static string Plist(params XElement[] values) => new XDocument(new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
        new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict", values))).ToString();
}
