using System.Diagnostics;

namespace SurgeSub.Core;

public sealed record CommandResult(int ExitCode, string Output);
public interface ICommandRunner
{
    Task<CommandResult> Run(string executable, string[] arguments, CancellationToken ct = default, int timeoutSeconds = 25);
}
public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> Run(string executable, string[] arguments, CancellationToken ct = default, int timeoutSeconds = 25)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var p = Process.Start(info) ?? throw new ClientException("无法启动所需的系统工具。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var output = p.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = p.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await p.WaitForExitAsync(deadline.Token);
            // Callers never surface raw stderr: it may contain subscription credentials.
            await error;
            return new(p.ExitCode, await output);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            throw new ClientException("系统或 Surge 操作超时，请稍后重试。");
        }
        catch { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
    }
}
