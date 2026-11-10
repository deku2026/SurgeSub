using System.Text.Json;
using SurgeSub.App;
using SurgeSub.Core;
using Xunit;

namespace SurgeSub.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly Storage storage = new(Path.Combine(Path.GetTempPath(), "surgesub-ui-" + Guid.NewGuid()));
    private readonly Runner runner = new();
    private string Agent => Path.Combine(storage.Root, "SurgeSub.Agent");
    private MainViewModel Model() => new(storage, runner, Agent);

    public MainViewModelTests()
    {
        Directory.CreateDirectory(storage.Root);
        File.WriteAllText(Agent, "fixture");
    }

    [Fact]
    public void AutomaticSynchronizationDefaultsOffForNewAndOlderSettings()
    {
        Assert.False(Model().AutoUpdate);
        Assert.False(Fixtures.Settings.AutoUpdate);
        const string json = "{\"subscriptionUrl\":\"https://example.test\",\"proxyApplication\":\"/Applications/Proxy.app\"}";
        Assert.False(JsonSerializer.Deserialize(json, ClientJson.Default.ClientSettings)!.AutoUpdate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningAndReopeningDoesNotSyncOrRegisterTasks(bool savedAutoUpdate)
    {
        storage.Save(storage.SettingsPath, Fixtures.Settings with { AutoUpdate = savedAutoUpdate }, ClientJson.Default.ClientSettings);
        var before = File.ReadAllText(storage.SettingsPath);
        var model = Model();
        await model.Load();
        model.ProxyPort = "9090";
        await model.Load();
        Assert.Equal("9090", model.ProxyPort); // Hiding and reopening must not reset unsaved edits.
        Assert.Equal(savedAutoUpdate, model.AutoUpdate);
        Assert.True(model.HasSavedSettings);
        Assert.Equal(before, File.ReadAllText(storage.SettingsPath));
        Assert.All(runner.Calls, c => Assert.Equal("/usr/bin/plutil", c.Executable));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualMenuSyncRunsExactlyOnceWithoutEnablingOrResumingBackgroundSync(bool autoUpdate)
    {
        storage.Save(storage.SettingsPath, Fixtures.Settings with { AutoUpdate = autoUpdate }, ClientJson.Default.ClientSettings);
        var before = File.ReadAllText(storage.SettingsPath);
        await Model().SyncSavedSettings();
        var call = Assert.Single(runner.Calls);
        Assert.Equal(Agent, call.Executable);
        Assert.Equal(new[] { "--once" }, call.Arguments);
        Assert.Equal(before, File.ReadAllText(storage.SettingsPath));
    }

    [Fact]
    public async Task ManualSyncWithoutSavedConfigurationDoesNotExecuteCommands()
    {
        var model = Model();
        await model.SyncSavedSettings();
        Assert.Empty(runner.Calls);
        Assert.Contains("请先", model.Status);
    }

    [Fact]
    public async Task BusyWindowDoesNotStartAnotherManualSync()
    {
        var model = Model();
        model.Busy = true;
        await model.SyncSavedSettings();
        Assert.Empty(runner.Calls);
    }

    public void Dispose() => Directory.Delete(storage.Root, true);
    private sealed class Runner : ICommandRunner
    {
        public List<(string Executable, string[] Arguments)> Calls { get; } = [];
        public Task<CommandResult> Run(string executable, string[] arguments, CancellationToken ct = default, int timeoutSeconds = 25)
        {
            Calls.Add((executable, arguments));
            return Task.FromResult(new CommandResult(0, executable == "/usr/bin/plutil" ? "{\"NetworkServices\":{}}" : ""));
        }
    }
}
