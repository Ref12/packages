using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Ref12.Feed.Tests;

/// <summary>Minimal stand-in for GitHub Releases: /{tag}/{asset} answers 302 to a "signed" URL that serves the bytes.</summary>
internal sealed class FakeReleases : IDisposable
{
    private readonly HttpListener _l = new();
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);   // "/a/b" -> disk path
    public string BaseUrl { get; }
    public int Redirects;
    public string Root { get; }

    public FakeReleases(string root)
    {
        Root = root;
        var t = new TcpListener(IPAddress.Loopback, 0); t.Start();
        var port = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _l.Prefixes.Add(BaseUrl);
        _l.Start();
        _ = Task.Run(Loop);
    }

    /// <summary>Serves dir/&lt;a&gt;/&lt;b&gt; as /&lt;a&gt;/&lt;b&gt;.</summary>
    public void Mount(string dir)
    {
        foreach (var sub in Directory.GetDirectories(dir))
            foreach (var file in Directory.GetFiles(sub))
                _files["/" + Path.GetFileName(sub) + "/" + Path.GetFileName(file)] = file;
    }

    public void Add(string url, string file) => _files[url] = file;

    private async Task Loop()
    {
        while (_l.IsListening)
        {
            HttpListenerContext c;
            try { c = await _l.GetContextAsync(); } catch { return; }
            try
            {
                var path = c.Request.Url!.AbsolutePath;
                if (path.StartsWith("/signed/", StringComparison.Ordinal))
                {
                    var key = path["/signed".Length..];
                    if (_files.TryGetValue(key, out var f))
                    {
                        var bytes = await File.ReadAllBytesAsync(f);
                        c.Response.ContentType = "application/octet-stream";
                        c.Response.ContentLength64 = bytes.Length;
                        await c.Response.OutputStream.WriteAsync(bytes);
                    }
                    else c.Response.StatusCode = 404;
                }
                else if (_files.ContainsKey(path))
                {
                    Interlocked.Increment(ref Redirects);
                    c.Response.StatusCode = 302;
                    c.Response.RedirectLocation = "/signed" + path + "?X-Amz-Signature=abc&X-Amz-Expires=300";
                }
                else c.Response.StatusCode = 404;
            }
            catch { /* client went away */ }
            finally { try { c.Response.Close(); } catch { } }
        }
    }

    public void Dispose() { _l.Close(); }
}

public class RestoreTests : IDisposable
{
    private readonly string _dir = TestUtil.TempDir();
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } p ? p : "dotnet";

    private (int Code, string Output) Restore(string consumerDir, string version, string id = "Ref12.Demo")
    {
        Directory.CreateDirectory(consumerDir);
        File.WriteAllText(Path.Combine(consumerDir, "c.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework>" +
            "<DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences></PropertyGroup>" +
            $"<ItemGroup><PackageReference Include=\"{id}\" Version=\"{version}\"/></ItemGroup></Project>");
        var psi = new ProcessStartInfo(Dotnet, "restore c.csproj")
        { WorkingDirectory = consumerDir, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["NUGET_PACKAGES"] = Path.Combine(consumerDir, "pkgs");
        psi.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(consumerDir, "http");
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000)) { p.Kill(true); return (-1, "timeout"); }
        return (p.ExitCode, o.Result + e.Result);
    }

    [Fact]
    public void Index_from_generator_restores_through_302_redirects()
    {
        var pkgs = Path.Combine(_dir, "releases");   // one dir per version release, like the real layout
        const string dep = "<group targetFramework=\".NETStandard2.0\"><dependency id=\"Ref12.Dep\" version=\"1.0.0\" /></group>";
        const string none = "<group targetFramework=\".NETStandard2.0\" />";
        TestUtil.MakeNupkg(Path.Combine(pkgs, "ref12.demo-1.0.0"), "Ref12.Demo", "1.0.0", none, "ref12.demo.1.0.0.nupkg");
        TestUtil.MakeNupkg(Path.Combine(pkgs, "ref12.demo-1.1.0"), "Ref12.Demo", "1.1.0", dep, "ref12.demo.1.1.0.nupkg");
        TestUtil.MakeNupkg(Path.Combine(pkgs, "ref12.demo-1.2.0"), "Ref12.Demo", "1.2.0", none, "ref12.demo.1.2.0.nupkg");
        TestUtil.MakeNupkg(Path.Combine(pkgs, "ref12.demo-2.0.0-beta.1"), "Ref12.Demo", "2.0.0-beta.1", none, "ref12.demo.2.0.0-beta.1.nupkg");
        TestUtil.MakeNupkg(Path.Combine(pkgs, "ref12.dep-1.0.0"), "Ref12.Dep", "1.0.0", none, "ref12.dep.1.0.0.nupkg");
        File.WriteAllText(Path.Combine(pkgs, "ref12.demo-1.2.0", "unlisted"), "");   // unlisted: hidden from floating ranges

        using var srv = new FakeReleases(_dir);
        var idx = Path.Combine(_dir, "idx");
        var ids = IndexBuilder.WriteAll(pkgs, idx, srv.BaseUrl);
        Assert.Equal(new[] { "ref12.demo", "ref12.dep" }, ids);

        // Service index lives in the "feed" release, as in the real feed.
        Directory.CreateDirectory(Path.Combine(_dir, "svc"));
        var svc = Path.Combine(_dir, "svc", "index.json");
        File.WriteAllText(svc, "{\"version\":\"3.0.0\",\"resources\":[{\"@id\":\"" + srv.BaseUrl + "\",\"@type\":\"RegistrationsBaseUrl/3.6.0\"}]}");
        srv.Add("/feed/index.json", svc);
        srv.Mount(pkgs);
        srv.Mount(idx);

        var cfg = "<configuration><packageSources><clear/><add key=\"rel\" value=\"" + srv.BaseUrl +
                  "feed/index.json\" protocolVersion=\"3\" allowInsecureConnections=\"true\"/></packageSources></configuration>";

        // 1. exact version with a dependency group: the dependency is resolved through the same feed.
        var c1 = Path.Combine(_dir, "c1"); Directory.CreateDirectory(c1);
        File.WriteAllText(Path.Combine(c1, "nuget.config"), cfg);
        var r1 = Restore(c1, "[1.1.0]");
        Assert.True(r1.Code == 0, r1.Output);
        var assets = File.ReadAllText(Path.Combine(c1, "obj", "project.assets.json"));
        Assert.Contains("\"Ref12.Demo/1.1.0\"", assets);
        Assert.Contains("\"Ref12.Dep/1.0.0\"", assets);
        Assert.True(File.Exists(Path.Combine(c1, "pkgs", "ref12.demo", "1.1.0", "ref12.demo.1.1.0.nupkg")));

        // 2. an unlisted version is still restorable when pinned (listed=false only hides it from search).
        var c2 = Path.Combine(_dir, "c2"); Directory.CreateDirectory(c2);
        File.WriteAllText(Path.Combine(c2, "nuget.config"), cfg);
        var r2 = Restore(c2, "[1.2.0]");
        Assert.True(r2.Code == 0, r2.Output);

        // 3. prerelease by exact version.
        var c3 = Path.Combine(_dir, "c3"); Directory.CreateDirectory(c3);
        File.WriteAllText(Path.Combine(c3, "nuget.config"), cfg);
        var r3 = Restore(c3, "2.0.0-beta.1");
        Assert.True(r3.Code == 0, r3.Output);

        Assert.True(srv.Redirects >= 4, "expected redirected index and package downloads, saw " + srv.Redirects);
    }
}
