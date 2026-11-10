namespace SurgeSub.Core;

public sealed class MacApplicationPicker(ICommandRunner commands)
{
    // Avalonia 11's file picker drops .app directories after the native panel accepts them.
    // Read NSOpenPanel's URL directly instead, including application bundles.
    private const string Script = """
        ObjC.import('AppKit');
        function run(argv) {
            const app = $.NSApplication.sharedApplication;
            app.setActivationPolicy($.NSApplicationActivationPolicyAccessory);
            const panel = $.NSOpenPanel.openPanel;
            panel.title = '选择代理 App 或核心可执行文件';
            panel.canChooseFiles = true;
            panel.canChooseDirectories = false;
            panel.treatsFilePackagesAsDirectories = false;
            panel.allowsMultipleSelection = false;
            panel.directoryURL = $.NSURL.fileURLWithPath(argv[0]);
            app.activateIgnoringOtherApps(true);
            if (panel.runModal !== $.NSModalResponseOK) return '';
            return ObjC.unwrap(panel.URL.path);
        }
        """;

    public async Task<string?> Pick(CancellationToken ct = default)
    {
        // Arguments are passed directly, without a shell or interpolation into the script.
        var result = await commands.Run("/usr/bin/osascript", ["-l", "JavaScript", "-e", Script, "/Applications"], ct, 600);
        if (result.ExitCode != 0)
            throw new ClientException("无法打开应用选择窗口，请重试或直接填写代理应用的完整路径。");
        var path = result.Output.TrimEnd('\r', '\n');
        if (path.Length == 0) return null; // Cancel must preserve the user's existing selection.
        return Validation.ProcessPath(path).TrimEnd('/');
    }
}
