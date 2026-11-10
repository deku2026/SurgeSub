using System.Xml.Linq;

namespace SurgeSub.Core;

public sealed class LoginService(ICommandRunner commands)
{
    public const string Label = "com.arcforges.surgesub.agent";
    public async Task Configure(bool startAtLogin, string agentPath, CancellationToken ct, bool autoUpdate = true)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/LaunchAgents");
        Directory.CreateDirectory(directory);
        var persistent = Path.Combine(directory, Label + ".plist");
        var uid = (await commands.Run("/usr/bin/id", ["-u"], ct)).Output.Trim();
        if (!int.TryParse(uid, out _)) throw new ClientException("无法确定登录用户。");
        var target = "gui/" + uid;
        await commands.Run("/bin/launchctl", ["bootout", target + "/" + Label], ct);
        if (File.Exists(persistent)) File.Delete(persistent);
        if (!autoUpdate) return;
        if (!File.Exists(agentPath)) throw new ClientException("找不到后台程序，请使用完整的 SurgeSub.app 安装包。");
        var root = Storage.DefaultRoot;
        Storage.PrivateDirectory(root);
        // A session-only launchd job keeps syncing after the window closes without enabling login startup.
        var path = startAtLogin ? persistent : Path.Combine(root, "session-agent.plist");
        var plist = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
                new XElement("key", "Label"), new XElement("string", Label),
                new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", agentPath), new XElement("string", "--watch")),
                new XElement("key", "RunAtLoad"), new XElement("true"),
                new XElement("key", "KeepAlive"), new XElement("true"),
                new XElement("key", "ThrottleInterval"), new XElement("integer", "30"),
                new XElement("key", "ProcessType"), new XElement("string", "Background"),
                new XElement("key", "StandardOutPath"), new XElement("string", Path.Combine(root, "agent-stdout.log")),
                new XElement("key", "StandardErrorPath"), new XElement("string", Path.Combine(root, "agent-stderr.log")))));
        Storage.AtomicWrite(path, plist.ToString());
        if ((await commands.Run("/bin/launchctl", ["bootstrap", target, path], ct)).ExitCode != 0)
            throw new ClientException("登录启动注册失败，请查看系统设置中的后台项目。");
    }
}
