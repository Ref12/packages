using System.IO.Compression;
using System.Xml.Linq;

namespace Ref12.Feed;

public sealed record Dependency(string Id, string Range);
public sealed record DependencyGroup(string? TargetFramework, List<Dependency> Dependencies);

/// <summary>The parts of a nuspec the registration index needs.</summary>
public sealed class Nuspec
{
    public string Id { get; init; } = "";
    public SemVer Version { get; init; } = null!;
    public string Authors { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? Tags { get; init; }
    public string? ProjectUrl { get; init; }
    public string? LicenseExpression { get; init; }
    public List<DependencyGroup> Groups { get; init; } = new();

    public static Nuspec Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var meta = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
                   ?? throw new InvalidDataException("nuspec has no <metadata> element.");
        string? Get(string name) => meta.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

        var id = Get("id");
        if (string.IsNullOrEmpty(id)) throw new InvalidDataException("nuspec has no <id>.");
        var ver = Get("version");
        if (!SemVer.TryParse(ver, out var v)) throw new InvalidDataException($"nuspec version '{ver}' is not a valid version.");

        var groups = new List<DependencyGroup>();
        var deps = meta.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (deps != null)
        {
            static Dependency Dep(XElement d) => new(
                (string?)d.Attribute("id") ?? throw new InvalidDataException("dependency without id."),
                string.IsNullOrWhiteSpace((string?)d.Attribute("version")) ? "(, )" : ((string)d.Attribute("version")!).Trim());
            var grouped = deps.Elements().Where(e => e.Name.LocalName == "group").ToList();
            foreach (var g in grouped)
                groups.Add(new DependencyGroup(
                    string.IsNullOrWhiteSpace((string?)g.Attribute("targetFramework")) ? null : ((string)g.Attribute("targetFramework")!).Trim(),
                    g.Elements().Where(e => e.Name.LocalName == "dependency").Select(Dep).ToList()));
            var flat = deps.Elements().Where(e => e.Name.LocalName == "dependency").Select(Dep).ToList();
            if (flat.Count > 0) groups.Add(new DependencyGroup(null, flat));
        }

        return new Nuspec
        {
            Id = id,
            Version = v!,
            Authors = Get("authors") ?? "",
            Description = Get("description") ?? "",
            Title = Get("title"),
            Summary = Get("summary"),
            Tags = Get("tags"),
            ProjectUrl = Get("projectUrl"),
            LicenseExpression = meta.Elements().FirstOrDefault(e => e.Name.LocalName == "license" && (string?)e.Attribute("type") == "expression")?.Value.Trim(),
            Groups = groups,
        };
    }

    public static Nuspec ReadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>Returns the nuspec text from the root of a .nupkg.</summary>
    public static string ReadNuspecText(string nupkgPath)
    {
        using var zip = ZipFile.OpenRead(nupkgPath);
        var entries = zip.Entries.Where(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count != 1) throw new InvalidDataException($"{Path.GetFileName(nupkgPath)} must contain exactly one root .nuspec (found {entries.Count}).");
        using var r = new StreamReader(entries[0].Open());
        return r.ReadToEnd();
    }

    public static Nuspec ReadNupkg(string nupkgPath) => Parse(ReadNuspecText(nupkgPath));
}
