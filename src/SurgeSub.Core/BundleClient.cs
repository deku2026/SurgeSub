using System.Net;
using System.Text.Json;

namespace SurgeSub.Core;

public sealed class BundleClient(HttpClient http)
{
    public async Task<CachedBundle> Fetch(Uri endpoint, CachedBundle? cached, CancellationToken ct)
    {
        var hash = ProfileGenerator.Hash(endpoint.AbsoluteUri);
        if (cached?.EndpointHash != hash) cached = null;
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.ParseAdd("SurgeSub/1.0");
        if (cached?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) return cached with { CheckedAt = DateTimeOffset.UtcNow };
        if (!response.IsSuccessStatusCode) throw new ClientException($"订阅更新失败：HTTP {(int)response.StatusCode}。已保留现有配置。");
        if (response.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new ClientException("订阅数据超过大小限制。");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + read > 16 * 1024 * 1024) throw new ClientException("订阅数据超过大小限制。");
            bytes.Write(buffer, 0, read);
        }
        var bundle = JsonSerializer.Deserialize(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), ClientJson.Default.ClientBundle)
            ?? throw new ClientException("订阅未返回配置包。");
        return new(hash, response.Headers.ETag?.ToString(), DateTimeOffset.UtcNow, bundle);
    }
}
