using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ref12.Feed;

public sealed class VersionEntry
{
    public Nuspec Spec { get; init; } = null!;
    public bool Listed { get; init; } = true;
    public string? Published { get; init; }
}

/// <summary>Builds NuGet v3 registration indexes (inline items) from per-version directories.</summary>
public static class IndexBuilder
{
    public const string DefaultBaseUrl = "https://github.com/Ref12/packages/releases/download/";
    public const int PageSize = 64;
    private const string Epoch = "1970-01-01T00:00:00Z";
    private const string UnlistedDate = "1900-01-01T00:00:00Z";

    /// <summary>
    /// Root layout: one directory per version release, holding "*.nuspec" (or else "*.nupkg"),
    /// optionally an "unlisted" marker file and "published.txt" (ISO timestamp).
    /// </summary>
    public static Dictionary<string, List<VersionEntry>> ReadRoot(string root, string? onlyId = null)
    {
        var result = new Dictionary<string, List<VersionEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            var spec = Directory.GetFiles(dir, "*.nuspec").FirstOrDefault();
            var nupkg = Directory.GetFiles(dir, "*.nupkg").FirstOrDefault();
            Nuspec ns;
            if (spec != null) ns = Nuspec.ReadFile(spec);
            else if (nupkg != null) ns = Nuspec.ReadNupkg(nupkg);
            else continue;
            if (onlyId != null && !string.Equals(ns.Id, onlyId, StringComparison.OrdinalIgnoreCase)) continue;
            var pub = Path.Combine(dir, "published.txt");
            var entry = new VersionEntry
            {
                Spec = ns,
                Listed = !File.Exists(Path.Combine(dir, "unlisted")),
                Published = File.Exists(pub) ? File.ReadAllText(pub).Trim() : null,
            };
            if (!result.TryGetValue(ns.Id, out var list)) result[ns.Id] = list = new();
            if (list.Any(e => e.Spec.Version.Equals(ns.Version)))
                throw new InvalidDataException($"Duplicate version {ns.Id} {ns.Version.Normalized} in {Path.GetFileName(dir)}.");
            list.Add(entry);
        }
        return result;
    }

    public static string PackageUrl(string baseUrl, string id, string version)
    {
        var idl = id.ToLowerInvariant(); var vl = version.ToLowerInvariant();
        return $"{baseUrl}{idl}-{vl}/{idl}.{vl}.nupkg";
    }

    public static JsonObject Build(string id, IEnumerable<VersionEntry> versions, string baseUrl = DefaultBaseUrl)
    {
        var sorted = versions.OrderBy(v => v.Spec.Version).ToList();
        var idl = id.ToLowerInvariant();
        var indexUrl = $"{baseUrl}{idl}/index.json";
        var pages = new JsonArray();
        foreach (var chunk in sorted.Chunk(PageSize))
        {
            var lower = chunk[0].Spec.Version.Normalized;
            var upper = chunk[^1].Spec.Version.Normalized;
            var pageUrl = $"{indexUrl}#page/{lower}/{upper}";
            var items = new JsonArray();
            foreach (var v in chunk) items.Add(Item(v, indexUrl, baseUrl));
            pages.Add(new JsonObject
            {
                ["@id"] = pageUrl,
                ["@type"] = "catalog:CatalogPage",
                ["commitTimeStamp"] = chunk.Max(c => c.Published ?? Epoch),
                ["count"] = chunk.Length,
                ["parent"] = indexUrl,
                ["lower"] = lower,
                ["upper"] = upper,
                ["items"] = items,
            });
        }
        return new JsonObject
        {
            ["@id"] = indexUrl,
            ["@type"] = new JsonArray("catalog:CatalogRoot", "PackageRegistration", "catalog:Permalink"),
            ["commitTimeStamp"] = sorted.Count == 0 ? Epoch : sorted.Max(c => c.Published ?? Epoch),
            ["count"] = pages.Count,
            ["items"] = pages,
        };
    }

    private static JsonObject Item(VersionEntry v, string indexUrl, string baseUrl)
    {
        var s = v.Spec;
        var ver = s.Version.Normalized;
        var leaf = $"{indexUrl}#{ver}";
        var content = PackageUrl(baseUrl, s.Id, ver);
        var groups = new JsonArray();
        var gi = 0;
        foreach (var g in s.Groups)
        {
            var go = new JsonObject { ["@id"] = $"{leaf}/dg/{gi++}", ["@type"] = "PackageDependencyGroup" };
            if (g.TargetFramework != null) go["targetFramework"] = g.TargetFramework;
            if (g.Dependencies.Count > 0)
            {
                var deps = new JsonArray();
                foreach (var d in g.Dependencies)
                    deps.Add(new JsonObject
                    {
                        ["@id"] = $"{leaf}/dg/{gi - 1}/{d.Id.ToLowerInvariant()}",
                        ["@type"] = "PackageDependency",
                        ["id"] = d.Id,
                        ["range"] = d.Range,
                        ["registration"] = $"{baseUrl}{d.Id.ToLowerInvariant()}/index.json",
                    });
                go["dependencies"] = deps;
            }
            groups.Add(go);
        }
        var entry = new JsonObject
        {
            ["@id"] = leaf,
            ["@type"] = "PackageDetails",
            ["id"] = s.Id,
            ["version"] = ver,
            ["authors"] = s.Authors,
            ["description"] = s.Description,
            ["listed"] = v.Listed,
            ["published"] = v.Listed ? (v.Published ?? Epoch) : UnlistedDate,
            ["packageContent"] = content,
            ["requireLicenseAcceptance"] = false,
        };
        if (s.Title != null) entry["title"] = s.Title;
        if (s.Summary != null) entry["summary"] = s.Summary;
        if (s.ProjectUrl != null) entry["projectUrl"] = s.ProjectUrl;
        if (s.LicenseExpression != null) entry["licenseExpression"] = s.LicenseExpression;
        if (!string.IsNullOrWhiteSpace(s.Tags))
            entry["tags"] = new JsonArray(s.Tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        entry["dependencyGroups"] = groups;
        return new JsonObject
        {
            ["@id"] = leaf,
            ["@type"] = "Package",
            ["commitTimeStamp"] = v.Published ?? Epoch,
            ["catalogEntry"] = entry,
            ["packageContent"] = content,
            ["registration"] = indexUrl,
        };
    }

    public static string Serialize(JsonObject o) =>
        o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";

    /// <summary>Writes out/&lt;idlower&gt;/index.json for every package (or just onlyId). Returns the lowercase ids written.</summary>
    public static List<string> WriteAll(string root, string outDir, string baseUrl, string? onlyId = null)
    {
        var written = new List<string>();
        foreach (var (id, list) in ReadRoot(root, onlyId).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var dir = Path.Combine(outDir, id.ToLowerInvariant());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "index.json"), Serialize(Build(id, list, baseUrl)));
            written.Add(id.ToLowerInvariant());
        }
        return written;
    }
}
