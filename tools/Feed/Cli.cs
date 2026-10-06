namespace Ref12.Feed;

/// <summary>
/// Commands:
///   verify --nupkg F --allowlist packages.json --repo owner/name [--tag T] [--nuget-org] --out-dir D
///   allowed-repo --allowlist packages.json --repo owner/name
///   index --root R --out O [--id ID] [--base-url URL]
/// </summary>
public static class Cli
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (args.Length == 0) { stderr.WriteLine("usage: Feed <verify|allowed-repo|index> [options]"); return 2; }
            var o = Options.Parse(args.Skip(1));
            return args[0] switch
            {
                "verify" => Verify(o, stdout, stderr),
                "allowed-repo" => AllowedRepo(o, stdout, stderr),
                "index" => Index(o, stdout),
                _ => Fail(stdout, stderr, $"unknown command '{args[0]}'"),
            };
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or ArgumentException or IOException or System.Xml.XmlException or System.Text.Json.JsonException)
        {
            return Fail(stdout, stderr, e.Message);
        }
    }

    private static int Fail(TextWriter stdout, TextWriter stderr, string msg)
    {
        // In Actions, ::error:: becomes an annotation; elsewhere plain stderr.
        stderr.WriteLine("error: " + msg);
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") stdout.WriteLine("::error::" + msg);
        return 1;
    }

    private static int AllowedRepo(Options o, TextWriter stdout, TextWriter stderr)
    {
        var list = Allowlist.Load(o.Req("allowlist"));
        var repo = o.Req("repo");
        if (list.IsRepoKnown(repo)) { stdout.WriteLine($"repo {repo} is listed in packages.json"); return 0; }
        return Fail(stdout, stderr, $"Repo '{repo}' is not listed for any package in packages.json; refusing to fetch or build from it.");
    }

    private static int Verify(Options o, TextWriter stdout, TextWriter stderr)
    {
        var nupkg = o.Req("nupkg");
        var repo = o.Req("repo");
        var spec = Nuspec.ReadNupkg(nupkg);
        var ver = spec.Version.Normalized;

        var tag = o.Get("tag");
        if (!string.IsNullOrEmpty(tag))
        {
            var t = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
            if (!SemVer.TryParse(t, out var tv) || !tv!.Equals(spec.Version))
                return Fail(stdout, stderr, $"Tag '{tag}' does not match the package version {ver} read from the nuspec ({spec.Id}).");
        }

        var (ok, msg) = Allowlist.Load(o.Req("allowlist")).Check(spec.Id, repo, o.Flag("nuget-org"));
        if (!ok) return Fail(stdout, stderr, msg);

        var idl = spec.Id.ToLowerInvariant(); var vl = ver.ToLowerInvariant();
        var outDir = o.Req("out-dir");
        Directory.CreateDirectory(outDir);
        File.Copy(nupkg, Path.Combine(outDir, $"{idl}.{vl}.nupkg"), true);
        File.WriteAllText(Path.Combine(outDir, $"{idl}.{vl}.nuspec"), Nuspec.ReadNuspecText(nupkg));

        var lines = new[] { $"id={spec.Id}", $"version={ver}", $"idl={idl}", $"verl={vl}", $"tag={idl}-{vl}" };
        foreach (var l in lines) stdout.WriteLine(l);
        var gho = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (!string.IsNullOrEmpty(gho)) File.AppendAllLines(gho, lines);
        return 0;
    }

    private static int Index(Options o, TextWriter stdout)
    {
        var ids = IndexBuilder.WriteAll(o.Req("root"), o.Req("out"), o.Get("base-url") ?? IndexBuilder.DefaultBaseUrl, o.Get("id"));
        foreach (var id in ids) stdout.WriteLine(id);
        return 0;
    }
}

public sealed class Options
{
    private readonly Dictionary<string, string> _v = new(StringComparer.OrdinalIgnoreCase);
    public static Options Parse(IEnumerable<string> args)
    {
        var o = new Options();
        var a = args.ToList();
        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].StartsWith("--")) throw new ArgumentException($"unexpected argument '{a[i]}'");
            var k = a[i][2..];
            if (i + 1 < a.Count && !a[i + 1].StartsWith("--")) o._v[k] = a[++i]; else o._v[k] = "true";
        }
        return o;
    }
    public string? Get(string k) => _v.TryGetValue(k, out var v) ? v : null;
    public string Req(string k) => Get(k) ?? throw new ArgumentException($"missing --{k}");
    public bool Flag(string k) => Get(k) is "true";
}
