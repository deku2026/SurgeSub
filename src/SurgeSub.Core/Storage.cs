using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SurgeSub.Core;

public sealed class Storage(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Generations => Path.Combine(Root, "generations");
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string StatusPath => Path.Combine(Root, "status.json");
    public string CachePath => Path.Combine(Root, "bundle.json");
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/SurgeSub");

    public static void PrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
            using (var writer = new StreamWriter(stream)) { writer.Write(text); writer.Flush(); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public T? Read<T>(string path, JsonTypeInfo<T> type) => File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), type) : default;
    public void Save<T>(string path, T value, JsonTypeInfo<T> type)
    {
        PrivateDirectory(Root);
        AtomicWrite(path, JsonSerializer.Serialize(value, type));
    }
    public ClientSettings? Settings() => Read(SettingsPath, ClientJson.Default.ClientSettings);
    public void Log(string message)
    {
        PrivateDirectory(Root);
        var path = Path.Combine(Root, "surgesub.log");
        if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".1", true);
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.ReadWrite };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.WriteLine($"{DateTimeOffset.Now:O} {message}");
    }
    public static string Error(Exception e) => e is ClientException ? e.Message : e is OperationCanceledException
        ? "操作超时，请检查网络或代理应用。" : $"操作失败（{e.GetType().Name}），请查看网络和 Surge 状态。";
}
