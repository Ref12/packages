using System.IO.Compression;
using System.Text;

namespace Ref12.Feed.Tests;

internal static class TestUtil
{
    public static string Fixture(params string[] parts) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(parts).ToArray());

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "feedtests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    public static string NuspecXml(string id, string version, string groups = "") =>
        "<?xml version=\"1.0\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata>" +
        $"<id>{id}</id><version>{version}</version><authors>t</authors><description>d</description>" +
        $"<dependencies>{groups}</dependencies></metadata></package>";

    /// <summary>Writes a minimal but restorable .nupkg (nuspec + an empty lib placeholder).</summary>
    public static string MakeNupkg(string dir, string id, string version, string groups = "", string? fileName = null)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName ?? $"{id}.{version}.nupkg");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Add($"{id}.nuspec", NuspecXml(id, version, groups));
        Add("lib/netstandard2.0/_._", "");
        return path;
    }

    public static (int Code, string Out, string Err) RunCli(params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var c = Cli.Run(args, o, e);
        return (c, o.ToString(), e.ToString());
    }
}
