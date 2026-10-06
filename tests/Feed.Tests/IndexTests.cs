using System.Text.Json.Nodes;
using Xunit;

namespace Ref12.Feed.Tests;

public class IndexTests
{
    private const string Base = "https://example.test/dl/";

    private static JsonObject BuildDemo()
    {
        var all = IndexBuilder.ReadRoot(TestUtil.Fixture("multi"), "Ref12.Demo");
        Assert.Single(all);
        return IndexBuilder.Build("Ref12.Demo", all["Ref12.Demo"], Base);
    }

    private static List<JsonNode> Entries(JsonObject root) =>
        root["items"]!.AsArray().SelectMany(p => p!["items"]!.AsArray()).Select(i => i!["catalogEntry"]!).ToList();

    [Fact]
    public void Versions_are_sorted_semver_with_prereleases_in_place()
    {
        var versions = Entries(BuildDemo()).Select(e => (string)e["version"]!).ToArray();
        Assert.Equal(new[]
        {
            "1.0.0", "1.2.0", "1.9.0", "1.10.0",
            "2.0.0-beta.1", "2.0.0-beta.2", "2.0.0-beta.11", "2.0.0-rc.1", "2.0.0",
        }, versions);
    }

    [Fact]
    public void Page_bounds_and_urls()
    {
        var root = BuildDemo();
        Assert.Equal(Base + "ref12.demo/index.json", (string)root["@id"]!);
        var page = root["items"]![0]!;
        Assert.Equal(9, (int)page["count"]!);
        Assert.Equal("1.0.0", (string)page["lower"]!);
        Assert.Equal("2.0.0", (string)page["upper"]!);
        var beta = Entries(root).Single(e => (string)e["version"]! == "2.0.0-beta.11");
        Assert.Equal(Base + "ref12.demo-2.0.0-beta.11/ref12.demo.2.0.0-beta.11.nupkg", (string)beta["packageContent"]!);
    }

    [Fact]
    public void Unlisted_marker_sets_listed_false_only_for_that_version()
    {
        var e = Entries(BuildDemo());
        Assert.False((bool)e.Single(x => (string)x["version"]! == "1.9.0")["listed"]!);
        Assert.All(e.Where(x => (string)x["version"]! != "1.9.0"), x => Assert.True((bool)x["listed"]!));
    }

    [Fact]
    public void Dependency_groups_per_framework_with_ranges()
    {
        var e = Entries(BuildDemo());
        var g0 = e.Single(x => (string)x["version"]! == "1.0.0")["dependencyGroups"]!.AsArray();
        Assert.Single(g0);
        Assert.Equal(".NETStandard2.0", (string)g0[0]!["targetFramework"]!);
        Assert.Null(g0[0]!["dependencies"]);

        var g = e.Single(x => (string)x["version"]! == "1.2.0")["dependencyGroups"]!.AsArray();
        Assert.Equal(2, g.Count);
        Assert.Equal("net8.0", (string)g[1]!["targetFramework"]!);
        var deps = g[1]!["dependencies"]!.AsArray();
        Assert.Equal("Ref12.Dep", (string)deps[0]!["id"]!);
        Assert.Equal("[1.0.0, 2.0.0)", (string)deps[0]!["range"]!);
        Assert.Equal("(, )", (string)deps[1]!["range"]!);   // no version => any
        Assert.Equal(Base + "ref12.dep/index.json", (string)deps[0]!["registration"]!);
    }

    [Fact]
    public void Flat_dependencies_become_a_group_without_framework()
    {
        var other = IndexBuilder.ReadRoot(TestUtil.Fixture("multi"), "ref12.other")["Ref12.Other"];
        var root = IndexBuilder.Build("Ref12.Other", other, Base);
        var g = Entries(root).Single()["dependencyGroups"]![0]!;
        Assert.Null(g["targetFramework"]);
        Assert.Equal("Ref12.Demo", (string)g["dependencies"]![0]!["id"]!);
    }

    [Fact]
    public void Large_package_is_split_in_pages()
    {
        var list = Enumerable.Range(1, 150).Select(i => new VersionEntry
        { Spec = Nuspec.Parse(TestUtil.NuspecXml("Big.Pkg", $"1.0.{i}")) }).Reverse().ToList();
        var root = IndexBuilder.Build("Big.Pkg", list, Base);
        Assert.Equal(3, (int)root["count"]!);
        var pages = root["items"]!.AsArray();
        Assert.Equal("1.0.1", (string)pages[0]!["lower"]!);
        Assert.Equal("1.0.64", (string)pages[0]!["upper"]!);
        Assert.Equal("1.0.150", (string)pages[2]!["upper"]!);
        Assert.Equal(22, (int)pages[2]!["count"]!);
    }

    [Fact]
    public void Reads_nupkg_when_no_nuspec_asset_exists_and_is_deterministic()
    {
        var root = TestUtil.TempDir();
        try
        {
            TestUtil.MakeNupkg(Path.Combine(root, "ref12.old-0.0.1"), "Ref12.Old", "0.0.1", "<group targetFramework=\".NETStandard2.0\" />", "ref12.old.0.0.1.nupkg");
            var o1 = Path.Combine(root, "o1"); var o2 = Path.Combine(root, "o2");
            var ids = IndexBuilder.WriteAll(Path.Combine(root), o1, Base);
            Assert.Equal(new[] { "ref12.old" }, ids);
            IndexBuilder.WriteAll(root, o2, Base);   // o1 is inside root but has no nuspec/nupkg, so it is skipped
            Assert.Equal(File.ReadAllText(Path.Combine(o1, "ref12.old", "index.json")), File.ReadAllText(Path.Combine(o2, "ref12.old", "index.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Duplicate_version_is_an_error()
    {
        var root = TestUtil.TempDir();
        try
        {
            TestUtil.MakeNupkg(Path.Combine(root, "a-1.0.0"), "A", "1.0.0");
            TestUtil.MakeNupkg(Path.Combine(root, "b-1.0.0"), "A", "1.0.0+x");
            Assert.Throws<InvalidDataException>(() => IndexBuilder.ReadRoot(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
