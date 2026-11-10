using System.Net;
using System.Text.Json;
using SurgeSub.Core;
using Xunit;

namespace SurgeSub.Tests;
public sealed class UpdateTests
{
    [Fact]
    public async Task Command_timeouts_are_failures_not_successful_agent_cancellation()
    {
        if (OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ClientException>(() => new CommandRunner().Run("/bin/sleep", ["10"], timeoutSeconds: 1));
    }
    [Fact]
    public async Task Conditional_fetch_keeps_cached_data_and_new_endpoints_do_not_reuse_credentials_or_etags()
    {
        var uri = new Uri("https://server.test/surgemac/client-bundle?token=a");
        var cache = new CachedBundle(ProfileGenerator.Hash(uri.AbsoluteUri), "\"r1\"", DateTimeOffset.UnixEpoch, Fixtures.Bundle);
        using var handler = new Handler(req =>
        {
            Assert.Equal("\"r1\"", req.Headers.IfNoneMatch.Single().ToString());
            return new(HttpStatusCode.NotModified);
        });
        var result = await new BundleClient(new HttpClient(handler)).Fetch(uri, cache, default);
        Assert.Same(cache.Bundle, result.Bundle);
        Assert.True(result.CheckedAt > cache.CheckedAt);
        using var changed = new Handler(req =>
        {
            Assert.Empty(req.Headers.IfNoneMatch);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Fixtures.Bundle, ClientJson.Default.ClientBundle)) };
        });
        var result2 = await new BundleClient(new HttpClient(changed)).Fetch(new("https://server.test/surgemac/client-bundle?token=b"), cache, default);
        Assert.NotEqual(cache.EndpointHash, result2.EndpointHash);
    }
    [Fact]
    public async Task Http_failures_do_not_become_empty_valid_bundles()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<ClientException>(() => new BundleClient(new HttpClient(handler)).Fetch(new("https://server.test/surgemac/client-bundle"), null, default));
    }
    [Fact]
    public async Task Failed_validation_leaves_the_previous_profile_untouched()
    {
        var root = Directory.CreateTempSubdirectory("surgesub-publish-").FullName;
        try
        {
            var destination = Path.Combine(root, "SurgeSub.conf"); File.WriteAllText(destination, "previous");
            var storage = new Storage(root);
            var profile = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings, Fixtures.Adapters, storage.Generations);
            var fake = new FakeSurge { FailValidation = true };
            await Assert.ThrowsAsync<ClientException>(() => new ProfilePublisher(storage, fake, destination).Publish(profile, true, default));
            Assert.Equal("previous", File.ReadAllText(destination)); Assert.Equal(0, fake.Applied);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Runtime_failure_restores_the_previous_main_file_and_resources_are_already_complete()
    {
        var root = Directory.CreateTempSubdirectory("surgesub-rollback-").FullName;
        try
        {
            var destination = Path.Combine(root, "SurgeSub.conf"); File.WriteAllText(destination, "previous");
            var storage = new Storage(root);
            var profile = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings, Fixtures.Adapters, storage.Generations);
            var fake = new FakeSurge { FailApply = true, OnApply = () => Assert.True(File.Exists(Path.Combine(storage.Generations, profile.Fingerprint, "all.conf"))) };
            await Assert.ThrowsAsync<ClientException>(() => new ProfilePublisher(storage, fake, destination).Publish(profile, true, default));
            Assert.Equal("previous", File.ReadAllText(destination)); Assert.Equal(1, fake.Applied);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Unchanged_content_does_not_reload_the_engine()
    {
        var root = Directory.CreateTempSubdirectory("surgesub-same-").FullName;
        try
        {
            var destination = Path.Combine(root, "SurgeSub.conf"); var storage = new Storage(root);
            var profile = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings, Fixtures.Adapters, storage.Generations);
            File.WriteAllText(destination, profile.Content);
            Directory.CreateDirectory(Path.Combine(storage.Generations, profile.Fingerprint));
            foreach (var (id, content) in profile.NodeFiles) File.WriteAllText(Path.Combine(storage.Generations, profile.Fingerprint, id), content);
            var fake = new FakeSurge();
            Assert.False(await new ProfilePublisher(storage, fake, destination).Publish(profile, false, default));
            Assert.Equal(0, fake.Applied);
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request)); }
    private sealed class FakeSurge : ISurgeController
    {
        public bool FailValidation, FailApply; public int Applied; public Action? OnApply;
        public Task Validate(string path, CancellationToken ct) => FailValidation ? Task.FromException(new ClientException("bad candidate")) : Task.CompletedTask;
        public Task<string> Current(CancellationToken ct) => Task.FromResult("SurgeSub");
        public Task Apply(string path, GeneratedProfile expected, bool activate, CancellationToken ct)
        { Applied++; OnApply?.Invoke(); return FailApply ? Task.FromException(new ClientException("reload failed")) : Task.CompletedTask; }
    }
}
