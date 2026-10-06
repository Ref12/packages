using System.Text.Json;

namespace Ref12.Feed;

public sealed class AllowEntry
{
    public string? Id { get; set; }
    public string? Prefix { get; set; }
    public List<string> Repos { get; set; } = new();
    public bool NugetOrg { get; set; }
    public string? Todo { get; set; }
}

public sealed class AllowFile { public List<AllowEntry> Packages { get; set; } = new(); }

/// <summary>packages.json: which source repos may publish which package ids (exact id or prefix).</summary>
public sealed class Allowlist
{
    private readonly List<AllowEntry> _entries;
    public Allowlist(IEnumerable<AllowEntry> entries) => _entries = entries.ToList();

    public static Allowlist Load(string path) => Parse(File.ReadAllText(path));

    public static Allowlist Parse(string json)
    {
        var f = JsonSerializer.Deserialize<AllowFile>(json, new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidDataException("packages.json is empty.");
        foreach (var e in f.Packages)
            if (string.IsNullOrWhiteSpace(e.Id) == string.IsNullOrWhiteSpace(e.Prefix))
                throw new InvalidDataException("Each packages.json entry needs exactly one of 'id' or 'prefix'.");
        return new Allowlist(f.Packages);
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Exact-id entries win; if none, every entry whose prefix the id starts with applies.</summary>
    private List<AllowEntry> Matching(string id)
    {
        var exact = _entries.Where(e => e.Id != null && Eq(e.Id, id)).ToList();
        if (exact.Count > 0) return exact;
        return _entries.Where(e => e.Prefix != null && id.StartsWith(e.Prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>True when the repo may publish at least one id (used before building anything from it).</summary>
    public bool IsRepoKnown(string repo) => _entries.Any(e => e.Repos.Any(r => Eq(r, repo)));

    public (bool Ok, string Message) Check(string id, string repo, bool nugetOrg = false)
    {
        var m = Matching(id);
        if (m.Count == 0)
            return (false, $"Package id '{id}' is not allowed: no id or prefix in packages.json matches it. Add an entry by pull request.");
        var allowed = m.SelectMany(e => e.Repos).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hit = m.Where(e => e.Repos.Any(r => Eq(r, repo))).ToList();
        if (hit.Count == 0)
            return (false, $"Repo '{repo}' is not allowed to publish '{id}'. Allowed source repos: {string.Join(", ", allowed)}.");
        if (nugetOrg && !hit.Any(e => e.NugetOrg))
            return (false, $"'{id}' is not enabled for nuget.org in packages.json (set \"nugetOrg\": true on its entry).");
        return (true, "ok");
    }
}
