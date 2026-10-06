using Xunit;

namespace Ref12.Feed.Tests;

public class VerifyTests : IDisposable
{
    private readonly string _dir = TestUtil.TempDir();
    private readonly string _allow;

    public VerifyTests()
    {
        _allow = Path.Combine(_dir, "packages.json");
        File.WriteAllText(_allow, @"{""packages"":[{""id"":""Ok.Pkg"",""repos"":[""o/r""]}]}");
    }
    public void Dispose() => Directory.Delete(_dir, true);

    private (int Code, string Out, string Err) Verify(string nupkg, string repo, string? tag = null, bool nuget = false)
    {
        var args = new List<string> { "verify", "--nupkg", nupkg, "--allowlist", _allow, "--repo", repo, "--out-dir", Path.Combine(_dir, "out") };
        if (tag != null) { args.Add("--tag"); args.Add(tag); }
        if (nuget) args.Add("--nuget-org");
        return TestUtil.RunCli(args.ToArray());
    }

    [Fact]
    public void Reads_id_and_version_from_nuspec_not_from_file_name()
    {
        // File name lies about both; the nuspec says Ok.Pkg 1.2.3+meta.
        var p = TestUtil.MakeNupkg(_dir, "Ok.Pkg", "1.2.3+meta", fileName: "Something.Else.9.9.9.nupkg");
        var r = Verify(p, "o/r", "v1.2.3");
        Assert.Equal(0, r.Code);
        Assert.Contains("id=Ok.Pkg", r.Out);
        Assert.Contains("version=1.2.3", r.Out);
        Assert.Contains("tag=ok.pkg-1.2.3", r.Out);
        Assert.True(File.Exists(Path.Combine(_dir, "out", "ok.pkg.1.2.3.nupkg")));
        Assert.True(File.Exists(Path.Combine(_dir, "out", "ok.pkg.1.2.3.nuspec")));
    }

    [Theory]
    [InlineData("1.2.3")] [InlineData("v1.2.3")] [InlineData("V1.2.3")]
    public void Tag_matching_versions_pass(string tag)
    {
        var p = TestUtil.MakeNupkg(_dir, "Ok.Pkg", "1.2.3");
        Assert.Equal(0, Verify(p, "o/r", tag).Code);
    }

    [Theory]
    [InlineData("v1.2.4")] [InlineData("release-1.2.3")] [InlineData("v1.2.3-rc.1")]
    public void Tag_mismatch_fails(string tag)
    {
        var p = TestUtil.MakeNupkg(_dir, "Ok.Pkg", "1.2.3");
        var r = Verify(p, "o/r", tag);
        Assert.Equal(1, r.Code);
        Assert.Contains("does not match", r.Err);
    }

    [Fact]
    public void Disallowed_repo_or_id_fails_with_message()
    {
        var p = TestUtil.MakeNupkg(_dir, "Ok.Pkg", "1.0.0");
        var r = Verify(p, "evil/fork");
        Assert.Equal(1, r.Code);
        Assert.Contains("not allowed to publish 'Ok.Pkg'", r.Err);
        Assert.False(Directory.Exists(Path.Combine(_dir, "out")));

        var q = TestUtil.MakeNupkg(Path.Combine(_dir, "q"), "Other.Pkg", "1.0.0");
        Assert.Contains("not allowed", Verify(q, "o/r").Err);
    }

    [Fact]
    public void Nuget_org_needs_opt_in_in_allowlist()
    {
        var p = TestUtil.MakeNupkg(_dir, "Ok.Pkg", "1.0.0");
        var r = Verify(p, "o/r", nuget: true);
        Assert.Equal(1, r.Code);
        Assert.Contains("nuget.org", r.Err);
    }

    [Fact]
    public void Broken_package_fails_cleanly()
    {
        var bad = Path.Combine(_dir, "bad.nupkg");
        File.WriteAllText(bad, "not a zip");
        Assert.Equal(1, Verify(bad, "o/r").Code);
    }

    [Fact]
    public void Allowed_repo_command()
    {
        Assert.Equal(0, TestUtil.RunCli("allowed-repo", "--allowlist", _allow, "--repo", "o/r").Code);
        Assert.Equal(1, TestUtil.RunCli("allowed-repo", "--allowlist", _allow, "--repo", "x/y").Code);
    }
}
