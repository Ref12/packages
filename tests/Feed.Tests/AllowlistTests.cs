using Xunit;

namespace Ref12.Feed.Tests;

public class AllowlistTests
{
    private static Allowlist Repo() =>
        Allowlist.Load(Path.Combine(FindRoot(), "packages.json"));

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "packages.json"))) d = d.Parent;
        return d?.FullName ?? throw new FileNotFoundException("packages.json not found above the test binaries.");
    }

    private const string Json = @"{ ""packages"": [
        { ""id"": ""A.B"", ""repos"": [""o/one""], ""nugetOrg"": true },
        { ""id"": ""A.B.Special"", ""repos"": [""o/two""] },
        { ""prefix"": ""A.B"", ""repos"": [""o/three""] },
        { ""prefix"": ""Z."", ""repos"": [""o/z""] } ] }";

    [Fact]
    public void Real_file_allows_probe_from_this_repo_only()
    {
        var a = Repo();
        Assert.True(a.Check("Ref12.FeedProbe", "ref12labs/packages").Ok);
        Assert.True(a.Check("ref12.feedprobe", "REF12LABS/Packages").Ok);   // case-insensitive
        var bad = a.Check("Ref12.FeedProbe", "evil/fork");
        Assert.False(bad.Ok);
        Assert.Contains("ref12labs/packages", bad.Message);
    }

    [Fact]
    public void Real_file_has_exact_wasmnative_entry()
    {
        var a = Repo();
        Assert.True(a.Check("Ref12.WasmNative", "ref12labs/packages").Ok);
        Assert.False(a.Check("Ref12.WasmNative", "ref12labs/dotnet-wasm-lab").Ok);
        Assert.False(a.Check("Ref12.WasmNativeX", "ref12labs/packages").Ok);
        Assert.False(a.Check("Ref12.Other", "ref12labs/packages").Ok);
    }

    [Fact]
    public void Unknown_id_message_is_clear()
    {
        var r = Allowlist.Parse(Json).Check("Nope", "o/one");
        Assert.False(r.Ok);
        Assert.Contains("not allowed", r.Message);
        Assert.Contains("packages.json", r.Message);
    }

    [Fact]
    public void Exact_entries_override_prefixes_and_prefixes_union()
    {
        var a = Allowlist.Parse(Json);
        Assert.True(a.Check("A.B", "o/one").Ok);
        Assert.False(a.Check("A.B", "o/three").Ok);            // exact entry exists: prefix ignored
        Assert.True(a.Check("A.B.Other", "o/three").Ok);       // prefix only
        Assert.False(a.Check("A.B.Other", "o/one").Ok);
        Assert.True(a.Check("A.B.Special", "o/two").Ok);
        Assert.False(a.Check("A.B.Special", "o/three").Ok);
    }

    [Fact]
    public void Nuget_org_requires_opt_in()
    {
        var a = Allowlist.Parse(Json);
        Assert.True(a.Check("A.B", "o/one", nugetOrg: true).Ok);
        var r = a.Check("A.B.Special", "o/two", nugetOrg: true);
        Assert.False(r.Ok);
        Assert.Contains("nuget.org", r.Message);
    }

    [Fact]
    public void IsRepoKnown_and_bad_entries()
    {
        var a = Allowlist.Parse(Json);
        Assert.True(a.IsRepoKnown("o/z"));
        Assert.False(a.IsRepoKnown("o/nobody"));
        Assert.Throws<InvalidDataException>(() => Allowlist.Parse(@"{""packages"":[{""repos"":[""x/y""]}]}"));
    }
}
