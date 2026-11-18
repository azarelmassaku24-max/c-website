using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CSharpQuest.Server.Progress;

/// <summary>
/// Bewaart de voortgang per profiel als JSON-bestand in de map 'data/progress'.
/// Het datamodel zelf wordt beheerd door de client (versie-veld voor migraties);
/// de server bewaakt grootte, geldigheid en maakt een backup van de vorige versie.
/// Later kan deze klasse vervangen worden door een database met accounts (zelfde interface).
/// </summary>
public interface IProgressStore
{
    Task<JsonNode?> LoadAsync(string profile, CancellationToken ct);
    Task SaveAsync(string profile, JsonNode data, CancellationToken ct);
    Task DeleteAsync(string profile, CancellationToken ct);
}

public sealed partial class FileProgressStore : IProgressStore
{
    readonly string _dir;
    readonly SemaphoreSlim _lock = new(1, 1);
    public const int MaxBytes = 4 * 1024 * 1024;

    public FileProgressStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config.GetValue<string>("Paths:Data") ?? "../../data";
        var root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured));
        _dir = Path.Combine(root, "progress");
        Directory.CreateDirectory(_dir);
    }

    public static bool ValidProfile(string profile) => ProfileRegex().IsMatch(profile);

    string PathFor(string profile) => Path.Combine(_dir, profile + ".json");

    public async Task<JsonNode?> LoadAsync(string profile, CancellationToken ct)
    {
        var path = PathFor(profile);
        if (!File.Exists(path)) return null;
        await _lock.WaitAsync(ct);
        try { return JsonNode.Parse(await File.ReadAllTextAsync(path, ct)); }
        catch (JsonException)
        {
            var backup = path + ".bak";
            return File.Exists(backup) ? JsonNode.Parse(await File.ReadAllTextAsync(backup, ct)) : null;
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(string profile, JsonNode data, CancellationToken ct)
    {
        var path = PathFor(profile);
        var json = data.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        if (json.Length > MaxBytes) throw new InvalidOperationException("Voortgang is te groot.");
        await _lock.WaitAsync(ct);
        try
        {
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct);
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(tmp, path, overwrite: true);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string profile, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = PathFor(profile);
            if (File.Exists(path)) File.Move(path, path + $".reset-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
        finally { _lock.Release(); }
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]{1,40}$")] private static partial Regex ProfileRegex();
}
