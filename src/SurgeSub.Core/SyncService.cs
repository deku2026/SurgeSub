using System.Text.Json;

namespace SurgeSub.Core;

public sealed class SyncService(Storage storage, MacSystem mac, BundleClient client, ProfilePublisher publisher)
{
    public async Task<SyncStatus> Run(bool manual, CancellationToken ct)
    {
        Storage.PrivateDirectory(storage.Root);
        using var gate = await Acquire(manual, ct);
        var settings = storage.Settings() ?? throw new ClientException("请先完成订阅、网卡、端口和代理应用设置。");
        Validation.Settings(settings);
        var endpoint = Validation.Endpoint(settings.SubscriptionUrl);
        var adapters = await mac.Adapters(ct);
        var cache = storage.Read(storage.CachePath, ClientJson.Default.CachedBundle);
        if (cache?.EndpointHash != ProfileGenerator.Hash(endpoint.AbsoluteUri)) cache = null;
        string? warning = null;
        var proxyAvailable = true;
        try { await mac.VerifyProxy(settings, ct); }
        catch (Exception e) when (!manual && e is not OperationCanceledException)
        { proxyAvailable = false; warning = Storage.Error(e) + " 本地代理已停用，等待所选应用恢复。"; }
        // Repair changed interfaces from the last valid data before any network-dependent download.
        if (cache is not null)
        {
            var offline = ProfileGenerator.Generate(cache.Bundle, settings, adapters, storage.Generations, proxyAvailable);
            await publisher.Publish(offline, manual, ct);
        }
        if (manual || cache is null || DateTimeOffset.UtcNow - cache.CheckedAt >= TimeSpan.FromSeconds(cache.Bundle.RefreshIntervalSeconds))
        {
            try
            {
                var downloaded = await client.Fetch(endpoint, cache, ct);
                var generated = ProfileGenerator.Generate(downloaded.Bundle, settings, adapters, storage.Generations, proxyAvailable);
                await publisher.Publish(generated, manual, ct);
                storage.Save(storage.CachePath, downloaded, ClientJson.Default.CachedBundle);
                cache = downloaded;
                storage.Log("订阅检查成功；版本 " + downloaded.Bundle.Revision[..Math.Min(12, downloaded.Bundle.Revision.Length)]);
            }
            catch (Exception e) when (cache is not null && e is not OperationCanceledException)
            { warning = Storage.Error(e) + " 继续使用上次成功的订阅数据。"; }
        }
        if (Validation.Device(Validation.Resolve(settings.Home!, adapters)) == Validation.MissingDevice)
            warning = "家宽网卡未连接或没有 IPv4 出口，家宽流量已阻断。";
        if (Validation.Device(Validation.Resolve(settings.Other!, adapters)) == Validation.MissingDevice)
            warning = "非家宽网卡未连接或没有 IPv4 出口，非家宽流量已阻断。";
        var status = new SyncStatus(DateTimeOffset.Now, warning is null, warning ?? "配置已同步，Surge 使用最新的本地网卡绑定。", cache?.Bundle.Revision);
        var old = storage.Read(storage.StatusPath, ClientJson.Default.SyncStatus);
        if (old?.Message != status.Message) storage.Log(status.Message);
        storage.Save(storage.StatusPath, status, ClientJson.Default.SyncStatus);
        return status;
    }
    private async Task<FileStream> Acquire(bool manual, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow.AddSeconds(manual ? 90 : 3);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(storage.Root, "sync.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < end) { await Task.Delay(300, ct); }
            catch (IOException) { throw new ClientException("另一次更新仍在进行，请稍后重试。"); }
        }
    }
}
