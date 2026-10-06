using System.Numerics;
using System.Text;

namespace Ref12.Feed;

/// <summary>SemVer 2.0 / NuGet version: 1-4 numeric parts, prerelease labels, build metadata (ignored in comparison).</summary>
public sealed class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    public int[] Parts { get; }
    public string[] Pre { get; }
    public string Meta { get; }

    private SemVer(int[] parts, string[] pre, string meta) { Parts = parts; Pre = pre; Meta = meta; }

    public bool IsPrerelease => Pre.Length > 0;

    public static SemVer Parse(string s) =>
        TryParse(s, out var v) ? v! : throw new FormatException($"'{s}' is not a valid version.");

    public static bool TryParse(string? s, out SemVer? v)
    {
        v = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        var meta = "";
        var plus = s.IndexOf('+');
        if (plus >= 0) { meta = s[(plus + 1)..]; s = s[..plus]; if (meta.Length == 0) return false; }
        var pre = Array.Empty<string>();
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..].Split('.');
            s = s[..dash];
            foreach (var p in pre)
                if (p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }
        var nums = s.Split('.');
        if (nums.Length is < 1 or > 4) return false;
        var parts = new int[4];
        for (var i = 0; i < nums.Length; i++)
        {
            if (nums[i].Length == 0 || !nums[i].All(char.IsAsciiDigit) || !int.TryParse(nums[i], out parts[i])) return false;
        }
        v = new SemVer(parts, pre, meta);
        return true;
    }

    /// <summary>NuGet normalized form: no build metadata, 4th part only when non-zero, always 3+ parts.</summary>
    public string Normalized
    {
        get
        {
            var sb = new StringBuilder($"{Parts[0]}.{Parts[1]}.{Parts[2]}");
            if (Parts[3] != 0) sb.Append('.').Append(Parts[3]);
            if (Pre.Length > 0) sb.Append('-').Append(string.Join('.', Pre));
            return sb.ToString();
        }
    }

    public override string ToString() => Normalized;

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < 4; i++)
        {
            var c = Parts[i].CompareTo(other.Parts[i]);
            if (c != 0) return c;
        }
        if (Pre.Length == 0 && other.Pre.Length == 0) return 0;
        if (Pre.Length == 0) return 1;       // release > prerelease
        if (other.Pre.Length == 0) return -1;
        var n = Math.Min(Pre.Length, other.Pre.Length);
        for (var i = 0; i < n; i++)
        {
            var a = Pre[i]; var b = other.Pre[i];
            var an = a.All(char.IsAsciiDigit); var bn = b.All(char.IsAsciiDigit);
            int c;
            if (an && bn) c = BigInteger.Parse(a).CompareTo(BigInteger.Parse(b));
            else if (an) c = -1;             // numeric < alphanumeric
            else if (bn) c = 1;
            else c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return Pre.Length.CompareTo(other.Pre.Length);
    }

    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVer s && Equals(s);
    public override int GetHashCode() => Normalized.ToLowerInvariant().GetHashCode();
}
