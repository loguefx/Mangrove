using System.Text;

namespace Mangrove.Server.Storage;

/// <summary>
/// Immutable value object that parses both UNC (<c>\\host\share\dir\file</c>) and
/// <c>smb://host/share/dir/file</c> URIs into their components, plus plain local paths.
/// Per spec §5 we never pass raw OS paths around — everything flows through this type.
/// </summary>
public sealed class StoragePath : IEquatable<StoragePath>
{
    public bool IsRemote { get; }

    /// <summary>SMB server host (only meaningful when <see cref="IsRemote"/>).</summary>
    public string Host { get; }

    /// <summary>SMB share name (only meaningful when <see cref="IsRemote"/>).</summary>
    public string Share { get; }

    /// <summary>
    /// Path relative to the share root (remote) using backslash separators with no leading
    /// separator, or the absolute local filesystem path (local).
    /// </summary>
    public string RelativePath { get; }

    private StoragePath(bool isRemote, string host, string share, string relativePath)
    {
        IsRemote = isRemote;
        Host = host;
        Share = share;
        RelativePath = relativePath;
    }

    public static StoragePath ParseLocal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Local path cannot be empty.", nameof(path));
        return new StoragePath(false, string.Empty, string.Empty, NormalizeLocal(path));
    }

    /// <summary>
    /// Parses a UNC or smb:// path. Throws <see cref="FormatException"/> when the input is not
    /// a recognizable remote path (missing host or share).
    /// </summary>
    public static StoragePath ParseRemote(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Remote path cannot be empty.", nameof(path));

        string remainder;
        var trimmed = path.Trim();

        if (trimmed.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            remainder = trimmed.Substring("smb://".Length);
        }
        else if (trimmed.StartsWith(@"\\"))
        {
            remainder = trimmed.Substring(2);
        }
        else if (trimmed.StartsWith("//"))
        {
            remainder = trimmed.Substring(2);
        }
        else
        {
            throw new FormatException(
                $"'{path}' is not a UNC (\\\\host\\share\\...) or smb:// path.");
        }

        // Unify separators to '/', drop empty segments.
        var segments = remainder
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
            throw new FormatException(
                $"'{path}' must include both a host and a share (e.g. \\\\host\\share\\path).");

        var host = segments[0];
        var share = segments[1];
        var relative = string.Join('\\', segments.Skip(2));
        return new StoragePath(true, host, share, relative);
    }

    /// <summary>Auto-detects local vs remote and parses accordingly.</summary>
    public static StoragePath Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        var t = path.TrimStart();
        if (t.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith(@"\\") || t.StartsWith("//"))
            return ParseRemote(path);
        return ParseLocal(path);
    }

    /// <summary>Returns a child path by appending <paramref name="name"/>.</summary>
    public StoragePath Combine(string name)
    {
        name = name.Trim('/', '\\');
        if (IsRemote)
        {
            var rel = string.IsNullOrEmpty(RelativePath) ? name : RelativePath + "\\" + name;
            return new StoragePath(true, Host, Share, rel);
        }
        return new StoragePath(false, string.Empty, string.Empty,
            NormalizeLocal(Path.Combine(RelativePath, name)));
    }

    /// <summary>Final path segment (file or folder name).</summary>
    public string Name
    {
        get
        {
            var p = RelativePath.TrimEnd('/', '\\');
            var idx = p.LastIndexOfAny(new[] { '/', '\\' });
            return idx >= 0 ? p.Substring(idx + 1) : p;
        }
    }

    /// <summary>The canonical string form (UNC for remote, OS path for local).</summary>
    public string Canonical()
    {
        if (!IsRemote) return RelativePath;
        var sb = new StringBuilder();
        sb.Append('\\').Append('\\').Append(Host).Append('\\').Append(Share);
        if (!string.IsNullOrEmpty(RelativePath))
            sb.Append('\\').Append(RelativePath);
        return sb.ToString();
    }

    private static string NormalizeLocal(string p) => p.Replace('/', Path.DirectorySeparatorChar);

    public override string ToString() => Canonical();

    public bool Equals(StoragePath? other)
    {
        if (other is null) return false;
        if (IsRemote != other.IsRemote) return false;
        if (IsRemote)
            return string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Share, other.Share, StringComparison.OrdinalIgnoreCase)
                && string.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase);
        return string.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as StoragePath);

    public override int GetHashCode() => IsRemote
        ? HashCode.Combine(true, Host.ToLowerInvariant(), Share.ToLowerInvariant(), RelativePath.ToLowerInvariant())
        : HashCode.Combine(false, RelativePath.ToLowerInvariant());
}
