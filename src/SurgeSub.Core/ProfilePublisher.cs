namespace SurgeSub.Core;

public sealed class ProfilePublisher(Storage storage, ISurgeController surge, string destination)
{
    public async Task<bool> Publish(GeneratedProfile profile, bool activate, CancellationToken ct)
    {
        var generation = Path.Combine(storage.Generations, profile.Fingerprint);
        if (File.Exists(destination) && File.ReadAllText(destination) == profile.Content
            && profile.NodeFiles.Keys.All(id => File.Exists(Path.Combine(generation, id))))
        {
            // A manual update also verifies the engine: matching disk content alone is insufficient.
            if (activate) await surge.Apply(destination, profile, true, ct);
            return false;
        }
        Storage.PrivateDirectory(generation);
        foreach (var (id, content) in profile.NodeFiles)
            Storage.AtomicWrite(Path.Combine(generation, id), content);
        var candidate = Path.Combine(generation, "candidate.conf");
        Storage.AtomicWrite(candidate, profile.Content);
        await surge.Validate(candidate, ct);
        var previous = File.Exists(destination) ? File.ReadAllText(destination) : null;
        var previousActive = await surge.Current(ct);
        if (previous is not null) Storage.AtomicWrite(Path.Combine(storage.Root, "previous.conf"), previous);
        // Resources are immutable and complete before the single main-file commit.
        Storage.AtomicWrite(destination, profile.Content);
        try { await surge.Apply(destination, profile, activate, ct); }
        catch
        {
            if (previous is not null) Storage.AtomicWrite(destination, previous);
            else if (File.Exists(destination)) File.Delete(destination);
            if (surge is SurgeController concrete)
            {
                try { await concrete.Restore(previousActive, CancellationToken.None); }
                catch (Exception e) { storage.Log("恢复 Surge 状态失败：" + Storage.Error(e)); }
            }
            throw;
        }
        storage.Log($"已应用配置 {profile.Fingerprint[..12]}；家宽 {profile.HomeDevice}，非家宽 {profile.OtherDevice}。");
        // Keep several complete generations and every resource referenced by current/rollback profiles.
        var retainedText = profile.Content + (previous ?? "");
        foreach (var old in new DirectoryInfo(storage.Generations).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Skip(3))
        {
            if (old.Name.Length != 64 || retainedText.Contains(old.Name, StringComparison.Ordinal)) continue;
            try { old.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return true;
    }
}
