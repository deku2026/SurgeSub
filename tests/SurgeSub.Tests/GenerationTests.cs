using System.Text.Json;
using SurgeSub.Core;
using Xunit;

namespace SurgeSub.Tests;
public sealed class GenerationTests
{
    [Fact]
    public void Local_choices_bind_directs_and_every_airport_node_without_proxy_chains()
    {
        var result = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings with { ProxyPort = 10808 }, Fixtures.Adapters, "/tmp/test gen");
        Assert.Contains("interface=en10, allow-other-interface=false", result.Content);
        Assert.Contains("interface=en1, allow-other-interface=false", result.Content);
        Assert.Contains("test-url=https://www.gstatic.com/generate_204, test-timeout=8", result.Content);
        Assert.Contains("本地代理 · 非家宽 = socks5, 127.0.0.1, 10808", result.Content);
        Assert.Contains("[Rule]\nPROCESS-NAME,/Applications/FlClash.app/,非家宽网卡直连\nPROTOCOL,DOH", result.Content);
        Assert.Contains("policy-path=\"/tmp/test gen/", result.Content);
        Assert.Contains("password=fixture-only", result.NodeFiles["all.conf"]);
        Assert.Contains("interface=en1, allow-other-interface=false", result.NodeFiles["all.conf"]);
        Assert.DoesNotContain("underlying-proxy", result.Content + result.NodeFiles["all.conf"]);
        Assert.DoesNotContain("MANAGED-CONFIG", result.Content);
        Assert.DoesNotContain("surgesub-resource:", result.Content);
    }
    [Fact]
    public void A_new_interface_number_changes_all_local_bindings_but_preserves_rules()
    {
        var before = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings, Fixtures.Adapters, "/tmp/g");
        var after = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings, [Fixtures.Home with { Device = "en12" }, Fixtures.Other with { Device = "en3" }], "/tmp/g");
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
        Assert.Contains("interface=en12", after.Content);
        Assert.Contains("interface=en3", after.NodeFiles["all.conf"]);
        Assert.DoesNotContain("interface=en1,", after.Content);
        Assert.Contains("PROCESS-NAME,/Applications/Firefox.app/,Global", after.Content);
    }
    [Fact]
    public void Disconnects_fail_closed_and_do_not_reuse_a_different_device_with_the_old_number()
    {
        var changed = Fixtures.Home with { ServiceId = "different-device", MacAddress = "EEEE" };
        var result = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings, [changed, Fixtures.Other with { Available = false }], "/tmp/g", false);
        Assert.Equal(Validation.MissingDevice, result.HomeDevice);
        Assert.Equal(Validation.MissingDevice, result.OtherDevice);
        Assert.Contains("interface=ssunavail0, allow-other-interface=false", result.NodeFiles["all.conf"]);
        Assert.Contains("本地代理 · 非家宽 = reject", result.Content);
    }
    [Theory]
    [InlineData("subscription")][InlineData("home")][InlineData("other")][InlineData("port")][InlineData("app")][InlineData("same")]
    public void Every_user_choice_is_required(string missing)
    {
        var s = missing switch
        {
            "subscription" => Fixtures.Settings with { SubscriptionUrl = "" }, "home" => Fixtures.Settings with { Home = null },
            "other" => Fixtures.Settings with { Other = null }, "port" => Fixtures.Settings with { ProxyPort = null },
            "app" => Fixtures.Settings with { ProxyApplication = "" }, _ => Fixtures.Settings with { Other = Fixtures.Settings.Home }
        };
        Assert.Throws<ClientException>(() => ProfileGenerator.Generate(Fixtures.Bundle, s, Fixtures.Adapters, "/tmp/g"));
    }
    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(65536)]
    public void Invalid_ports_are_rejected(int port) => Assert.Throws<ClientException>(() => Validation.Settings(Fixtures.Settings with { ProxyPort = port }));
    [Theory]
    [InlineData("/Applications/Surge.app")][InlineData("/Applications/Evil.app/,DIRECT")][InlineData("relative")][InlineData("/Applications/*.app")]
    public void Invalid_process_paths_cannot_inject_rules(string app) => Assert.Throws<ClientException>(() => Validation.ProcessPath(app));
    [Fact]
    public void Bundle_prefix_has_a_directory_boundary_and_core_files_match_exactly()
    {
        Assert.True(MacSystem.OwnsProcess("/Applications/FlClash.app/", "/Applications/FlClash.app/Contents/MacOS/FlClashCore"));
        Assert.False(MacSystem.OwnsProcess("/Applications/FlClash.app/", "/Applications/FlClash.app-other/core"));
        Assert.False(MacSystem.OwnsProcess("/opt/core", "/opt/core-old"));
    }
    [Fact]
    public void Runtime_verification_does_not_confuse_en1_and_en10()
    {
        Assert.False(SurgeController.BindingsMatch("Other = direct, interface=en10", "Other = direct, interface=en1"));
        Assert.True(SurgeController.BindingsMatch("Other = direct, interface=en1", "Other = direct, interface=en1, allow-other-interface=false"));
    }
    [Fact]
    public void Privacy_masked_mac_addresses_cannot_identify_a_replacement_device()
    {
        var choice = Fixtures.Home.Choice with { MacAddress = "020000000000" };
        Assert.Null(Validation.Resolve(choice, [Fixtures.Home with { ServiceId = "new", MacAddress = "020000000000" }]));
    }
    [Theory]
    [InlineData("surgeios")][InlineData("surgemac")]
    public void Existing_subscription_addresses_keep_their_encoded_authentication(string platform)
    {
        var uri = Validation.Endpoint($"https://server.test/prefix/{platform}/combined?token=a%2Bb%26c");
        Assert.Equal("/prefix/surgemac/client-bundle", uri.AbsolutePath);
        Assert.Equal("?token=a%2Bb%26c", uri.Query);
    }
    [Fact]
    public void Unknown_bundle_versions_and_escaping_resource_names_are_rejected()
    {
        Assert.Throws<ClientException>(() => ProfileGenerator.Generate(Fixtures.Bundle with { SchemaVersion = 2 }, Fixtures.Settings, Fixtures.Adapters, "/tmp/g"));
        Assert.Throws<ClientException>(() => ProfileGenerator.Generate(Fixtures.Bundle with { NodeResources = [new("../escape.conf", Fixtures.Bundle.NodeResources[0].Policies)] }, Fixtures.Settings, Fixtures.Adapters, "/tmp/g"));
        Assert.Throws<ClientException>(() => ProfileGenerator.Generate(Fixtures.Bundle with { NodeResources = [] }, Fixtures.Settings, Fixtures.Adapters, "/tmp/g"));
    }
    [Fact]
    public void Duplicate_or_missing_resources_never_produce_partial_configuration()
    {
        Assert.Throws<ClientException>(() => ProfileGenerator.Generate(Fixtures.Bundle with { NodeResources = [Fixtures.Bundle.NodeResources[0], Fixtures.Bundle.NodeResources[0]] }, Fixtures.Settings, Fixtures.Adapters, "/tmp/g"));
        var sections = Fixtures.Bundle.Sections.Select(s => s.Name == "Proxy Group" ? s with { Lines = ["Global = select, policy-path=surgesub-resource:missing.conf"] } : s).ToArray();
        Assert.Throws<ClientException>(() => ProfileGenerator.Generate(Fixtures.Bundle with { Sections = sections }, Fixtures.Settings, Fixtures.Adapters, "/tmp/g"));
    }
    [Fact]
    public async Task Native_surge_accepts_generated_configuration_and_resources()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists(SurgeController.Cli)) return;
        var root = Directory.CreateTempSubdirectory("surgesub-native-").FullName;
        try
        {
            var result = ProfileGenerator.Generate(Fixtures.Bundle, Fixtures.Settings, Fixtures.Adapters, root);
            Directory.CreateDirectory(Path.Combine(root, result.Fingerprint));
            foreach (var (name, content) in result.NodeFiles) File.WriteAllText(Path.Combine(root, result.Fingerprint, name), content);
            var path = Path.Combine(root, "test.conf"); File.WriteAllText(path, result.Content);
            await new SurgeController(new CommandRunner()).Validate(path, default);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Actual_SubVault_fixture_is_consumed_and_checked_when_supplied()
    {
        var path = Environment.GetEnvironmentVariable("SUBVAULT_CLIENT_FIXTURE");
        if (path is null) return;
        var bundle = JsonSerializer.Deserialize(File.ReadAllText(path), ClientJson.Default.ClientBundle)!;
        var root = Directory.CreateTempSubdirectory("surgesub-contract-").FullName;
        try
        {
            var result = ProfileGenerator.Generate(bundle, Fixtures.Settings, Fixtures.Adapters, root);
            Directory.CreateDirectory(Path.Combine(root, result.Fingerprint));
            foreach (var (id, content) in result.NodeFiles) File.WriteAllText(Path.Combine(root, result.Fingerprint, id), content);
            Assert.True(result.NodeFiles.Count > 1);
            Assert.Contains("PROCESS-NAME,/Applications/Firefox.app/,Firefox", result.Content);
            var candidate = Path.Combine(root, "candidate.conf"); File.WriteAllText(candidate, result.Content);
            if (OperatingSystem.IsMacOS()) await new SurgeController(new CommandRunner()).Validate(candidate, default);
        }
        finally { Directory.Delete(root, true); }
    }
}
