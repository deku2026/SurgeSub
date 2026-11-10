using SurgeSub.Core;
using Xunit;

namespace SurgeSub.Tests;

public sealed class MacApplicationPickerTests
{
    [Theory]
    [InlineData("/Applications/FlClash.app/\n", "/Applications/FlClash.app")]
    [InlineData("/Users/test/Applications/Proxy 客户端.app/\n", "/Users/test/Applications/Proxy 客户端.app")]
    [InlineData("/Applications/FlClash.app/Contents/MacOS/FlClashCore\n", "/Applications/FlClash.app/Contents/MacOS/FlClashCore")]
    public async Task ReturnsApplicationBundlesAndStandaloneExecutables(string selected, string expected)
    {
        var runner = new PickerRunner(new(0, selected));
        Assert.Equal(expected, await new MacApplicationPicker(runner).Pick());
        Assert.Equal("/usr/bin/osascript", runner.Executable);
        Assert.Equal("/Applications", runner.Arguments![^1]);
    }

    [Fact]
    public async Task CancellationHasNoReplacementPath()
        => Assert.Null(await new MacApplicationPicker(new PickerRunner(new(0, "\n"))).Pick());

    [Fact]
    public async Task NativeFailureDoesNotReturnAReplacementOrExposeOutput()
    {
        var error = await Assert.ThrowsAsync<ClientException>(() =>
            new MacApplicationPicker(new PickerRunner(new(1, "private diagnostic"))).Pick());
        Assert.DoesNotContain("private diagnostic", error.Message);
    }

    [Theory]
    [InlineData("/Applications/Surge.app/\n")]
    [InlineData("not-an-absolute-path\n")]
    public async Task InvalidSelectionIsRejected(string selected)
        => await Assert.ThrowsAsync<ClientException>(() => new MacApplicationPicker(new PickerRunner(new(0, selected))).Pick());

    private sealed class PickerRunner(CommandResult result) : ICommandRunner
    {
        public string? Executable;
        public string[]? Arguments;
        public Task<CommandResult> Run(string executable, string[] arguments, CancellationToken ct = default, int timeoutSeconds = 25)
        {
            Executable = executable; Arguments = arguments;
            return Task.FromResult(result);
        }
    }
}
