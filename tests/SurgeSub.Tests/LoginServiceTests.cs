using System.Xml.Linq;
using SurgeSub.Core;
using Xunit;

namespace SurgeSub.Tests;

public sealed class LoginServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "surgesub-login-" + Guid.NewGuid());
    private readonly Runner runner = new();
    private string Agents => Path.Combine(root, "LaunchAgents");
    private string Data => Path.Combine(root, "data");
    private string Agent => Path.Combine(root, "Apps & Tools/SurgeSub.app/Contents/MacOS/SurgeSub.Agent");
    private LoginService Service => new(runner, Data, Agents);

    public LoginServiceTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Agent)!);
        File.WriteAllText(Agent, "fixture");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Agent)!, "SurgeSub.App"), "fixture");
    }

    [Fact]
    public async Task LoginIconDoesNotEnableSynchronization()
    {
        await Service.Configure(true, Agent, CancellationToken.None);
        var menu = ReadJob(Path.Combine(Agents, LoginService.MenuLabel + ".plist"));
        Assert.Equal(new[] { "/usr/bin/open", "-g", Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Agent)!, "../..")), "--args", "--background" },
            menu["ProgramArguments"].Elements().Select(e => e.Value));
        Assert.False(menu.ContainsKey("KeepAlive"));
        Assert.False(File.Exists(Path.Combine(Agents, LoginService.Label + ".plist")));
        Assert.False(File.Exists(Path.Combine(Data, "session-agent.plist")));
        Assert.DoesNotContain(runner.Calls, c => c.Arguments[0] == "bootstrap");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyExplicitOptInStartsAutomaticSync(bool login)
    {
        await Service.Configure(login, Agent, CancellationToken.None, autoUpdate: true);
        var path = login ? Path.Combine(Agents, LoginService.Label + ".plist") : Path.Combine(Data, "session-agent.plist");
        var job = ReadJob(path);
        Assert.Equal(new[] { Agent, "--watch" }, job["ProgramArguments"].Elements().Select(e => e.Value));
        Assert.Equal("60", job["ExitTimeOut"].Value);
        Assert.Single(runner.Calls, c => c.Arguments.SequenceEqual(new[] { "bootstrap", "gui/501", path }));
        Assert.Equal(login, File.Exists(Path.Combine(Agents, LoginService.MenuLabel + ".plist")));
    }

    [Fact]
    public async Task QuitStopsSessionWithoutDeletingLoginPreferencesOrTouchingSurge()
    {
        await Service.Configure(true, Agent, CancellationToken.None, true);
        var path = Path.Combine(Agents, LoginService.Label + ".plist");
        var before = File.ReadAllText(path);
        runner.Calls.Clear();
        await Service.Stop();
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Contains(runner.Calls, c => c.Arguments.SequenceEqual(new[] { "bootout", "gui/501/" + LoginService.Label }) && c.Timeout == 90);
        Assert.All(runner.Calls, c => Assert.Contains(c.Executable, new[] { "/usr/bin/id", "/bin/launchctl" }));
        Assert.False(await Service.IsRunning());
    }

    [Fact]
    public async Task StoppingAbsentTaskIsSafeAndStatusChecksDoNotStartIt()
    {
        Assert.False(await Service.IsRunning());
        await Service.Stop();
        Assert.DoesNotContain(runner.Calls, c => c.Arguments[0] is "bootout" or "bootstrap");
    }

    [Fact]
    public async Task FailedStopPreservesPersistentConfiguration()
    {
        await Service.Configure(true, Agent, CancellationToken.None, true);
        var path = Path.Combine(Agents, LoginService.Label + ".plist");
        var before = File.ReadAllText(path);
        runner.FailStop = true;
        await Assert.ThrowsAsync<ClientException>(() => Service.Configure(false, Agent, CancellationToken.None));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task DisablingBothStopsTasksAndRemovesLoginFiles()
    {
        await Service.Configure(true, Agent, CancellationToken.None, true);
        runner.Calls.Clear();
        await Service.Configure(false, Agent, CancellationToken.None);
        Assert.False(await Service.IsRunning());
        Assert.Empty(Directory.GetFiles(Agents));
        Assert.DoesNotContain(runner.Calls, c => c.Arguments[0] == "bootstrap");
    }

    private static Dictionary<string, XElement> ReadJob(string path)
        => XDocument.Load(path).Root!.Element("dict")!.Elements().Chunk(2).ToDictionary(p => p[0].Value, p => p[1]);
    public void Dispose() => Directory.Delete(root, true);

    private sealed class Runner : ICommandRunner
    {
        public List<(string Executable, string[] Arguments, int Timeout)> Calls { get; } = [];
        private readonly HashSet<string> active = [];
        public bool FailStop;
        public Task<CommandResult> Run(string executable, string[] arguments, CancellationToken ct = default, int timeoutSeconds = 25)
        {
            Calls.Add((executable, arguments, timeoutSeconds));
            if (executable == "/usr/bin/id") return Task.FromResult(new CommandResult(0, "501\n"));
            Assert.Equal("/bin/launchctl", executable);
            switch (arguments[0])
            {
                case "print": return Task.FromResult(new CommandResult(active.Contains(arguments[1]) ? 0 : 113, ""));
                case "bootstrap": active.Add(arguments[1] + "/" + LoginService.Label); break;
                case "bootout":
                    if (FailStop) return Task.FromResult(new CommandResult(1, ""));
                    active.Remove(arguments[1]); break;
                default: throw new InvalidOperationException("Unexpected command");
            }
            return Task.FromResult(new CommandResult(0, ""));
        }
    }
}
