using System.Collections.Concurrent;
using System.Text.Json;
using AiLocal.Core.Discovery;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

public sealed record KnownHost(
    string Id,
    string Name,
    string Endpoint,
    DateTimeOffset LastSeen,
    bool IsExplicit);

/// <summary>
/// Overseer-side registry of every Host observed through discovery or explicit
/// configuration. Hosts survive Overseer restarts; live topology refreshes map
/// each Worker back to the Host that owns it.
/// </summary>
public sealed class HostRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, KnownHost> _hosts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _workerHosts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _saveGate = new();
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;

    public HostRegistry()
    {
        foreach (var host in Load())
            _hosts[NormalizeEndpoint(host.Endpoint)] = host with
            {
                Endpoint = NormalizeEndpoint(host.Endpoint)
            };
    }

    public IReadOnlyList<KnownHost> All =>
        _hosts.Values
            .OrderByDescending(host => host.LastSeen)
            .ThenBy(host => host.Name)
            .ToList();

    public string? PrimaryEndpoint => All.FirstOrDefault()?.Endpoint;

    public void Upsert(DiscoveryBeacon beacon)
    {
        if (beacon.Role != NodeRole.Host)
            return;

        Upsert(beacon.NodeId, beacon.Name, beacon.Endpoint, isExplicit: false);
    }

    public void UpsertExplicit(string endpoint)
    {
        var normalized = NormalizeEndpoint(endpoint);
        var id = $"explicit-{Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized)))[..10].ToLowerInvariant()}";
        Upsert(id, normalized, normalized, isExplicit: true);
    }

    public void UpdateIdentity(string endpoint, string id, string name)
    {
        var normalized = NormalizeEndpoint(endpoint);
        _hosts.AddOrUpdate(
            normalized,
            _ => new KnownHost(id, name, normalized, DateTimeOffset.UtcNow, false),
            (_, existing) => existing with
            {
                Id = id,
                Name = name,
                LastSeen = DateTimeOffset.UtcNow
            });
        RemoveAliases(id, normalized);
        Save(force: true);
    }

    public void MapWorker(string workerId, string hostEndpoint)
    {
        _workerHosts[workerId] = NormalizeEndpoint(hostEndpoint);
    }

    public string? HostForWorker(string workerId) =>
        _workerHosts.TryGetValue(workerId, out var endpoint) ? endpoint : null;

    /// <summary>Forgets a Host this Overseer has seen (by its "host-{id}" or
    /// raw id). Lets an operator clean up a stale entry from an old test
    /// setup or reconfiguration - there's no automatic expiry, since a Host
    /// that's merely offline right now (network blip, machine asleep) should
    /// still show up once it's back, not silently vanish.</summary>
    public bool Remove(string id)
    {
        var raw = id.StartsWith("host-", StringComparison.OrdinalIgnoreCase) ? id[5..] : id;
        var match = _hosts.FirstOrDefault(pair => pair.Value.Id.Equals(raw, StringComparison.OrdinalIgnoreCase));
        if (match.Key is null)
            return false;

        var removed = _hosts.TryRemove(match.Key, out _);
        if (removed)
            Save(force: true);
        return removed;
    }

    private void Upsert(string id, string name, string endpoint, bool isExplicit)
    {
        var normalized = NormalizeEndpoint(endpoint);
        var added = false;
        _hosts.AddOrUpdate(
            normalized,
            _ =>
            {
                added = true;
                return new KnownHost(id, name, normalized, DateTimeOffset.UtcNow, isExplicit);
            },
            (_, existing) => existing with
            {
                Id = id,
                Name = name,
                LastSeen = DateTimeOffset.UtcNow,
                IsExplicit = existing.IsExplicit || isExplicit
            });
        RemoveAliases(id, normalized);
        Save(force: added);
    }

    private void RemoveAliases(string id, string keepEndpoint)
    {
        foreach (var pair in _hosts)
        {
            if (!pair.Key.Equals(keepEndpoint, StringComparison.OrdinalIgnoreCase) &&
                pair.Value.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                _hosts.TryRemove(pair.Key, out _);
        }
    }

    private void Save(bool force)
    {
        lock (_saveGate)
        {
            if (!force && DateTimeOffset.UtcNow - _lastSave < TimeSpan.FromSeconds(30))
                return;

            Directory.CreateDirectory(SettingsPaths.DataDirectory);
            var path = Path.Combine(SettingsPaths.DataDirectory, "overseer-hosts.json");
            var temporary = path + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(All, JsonOptions);

            using (var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            _lastSave = DateTimeOffset.UtcNow;
        }
    }

    private static IReadOnlyList<KnownHost> Load()
    {
        try
        {
            var path = Path.Combine(SettingsPaths.DataDirectory, "overseer-hosts.json");
            if (!File.Exists(path))
                return [];
            return JsonSerializer.Deserialize<List<KnownHost>>(
                File.ReadAllText(path),
                JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeEndpoint(string endpoint) => endpoint.Trim().TrimEnd('/');
}
