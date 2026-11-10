using System.Text.Json.Serialization;

namespace SurgeSub.Core;

public sealed record ClientBundle(int SchemaVersion, string Revision, int RefreshIntervalSeconds,
    ClientBindings Bindings, ClientSection[] Sections, ClientResource[] NodeResources);
public sealed record ClientBindings(string HomePolicy, string OtherPolicy, string LocalProxyPolicy);
public sealed record ClientSection(string Name, string[] Lines);
public sealed record ClientResource(string Id, string[] Policies);
public sealed record AdapterChoice(string ServiceId, string Hardware, string MacAddress, string Label);
public sealed record AdapterInfo(string ServiceId, string Name, string Hardware, string Device,
    string MacAddress, bool Available, string[] Addresses)
{
    public string Display => $"{Name} · {Device} · {(Available ? string.Join(", ", Addresses) : "未连接")}";
    public AdapterChoice Choice => new(ServiceId, Hardware, MacAddress, Name);
}
public sealed record ClientSettings(string SubscriptionUrl, AdapterChoice? Home, AdapterChoice? Other,
    int? ProxyPort, string ProxyApplication, bool AutoUpdate = false, bool StartAtLogin = false);
public sealed record CachedBundle(string EndpointHash, string? ETag, DateTimeOffset CheckedAt, ClientBundle Bundle);
public sealed record SyncStatus(DateTimeOffset At, bool Success, string Message, string? Revision = null);
public sealed record GeneratedProfile(string Content, Dictionary<string, string> NodeFiles, string Fingerprint,
    string HomeDevice, string OtherDevice);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ClientBundle))]
[JsonSerializable(typeof(ClientSettings))]
[JsonSerializable(typeof(CachedBundle))]
[JsonSerializable(typeof(SyncStatus))]
[JsonSerializable(typeof(AdapterInfo[]))]
public partial class ClientJson : JsonSerializerContext;

public sealed class ClientException(string message) : Exception(message);
