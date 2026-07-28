namespace DistributionSystem.Application.Configuration;

/// <summary>
/// Single source of truth for turning FileStorage:RootPath (and the Public/PrivateDirectory
/// names under it) into absolute, validated physical paths. Every consumer — the DI-registered
/// <c>PhysicalFileStorageService</c>, the public static-file <c>PhysicalFileProvider</c> in
/// Program.cs, and startup validation — must call this instead of resolving paths independently,
/// so a relative RootPath (e.g. MonsterASP's <c>../private</c>, a sibling of the deployed
/// wwwroot folder) is always combined against the same ContentRootPath and always ends up
/// absolute before it reaches anything that requires an absolute path (like
/// <see cref="Microsoft.Extensions.FileProviders.PhysicalFileProvider"/>).
/// </summary>
public static class FileStorageRootResolver
{
    /// <summary>
    /// Resolves FileStorage:RootPath to an absolute path:
    /// - Absolute configured value: normalized as-is via <see cref="Path.GetFullPath(string)"/>.
    /// - Relative configured value (e.g. "../private" or ".local-storage"): combined against
    ///   <paramref name="contentRootPath"/> first, then normalized — this is what makes a
    ///   MonsterASP-style sibling directory work without ever hardcoding an OS-specific path.
    /// - Blank value: only allowed in Development, where it falls back to a fixed, non-machine-
    ///   specific folder under the content root; blank outside Development fails startup clearly.
    /// </summary>
    public static string ResolveRoot(string? configuredRoot, string contentRootPath, bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var basePath = Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(contentRootPath, configuredRoot);
            var resolved = Path.GetFullPath(basePath);

            if (string.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException("FileStorage:RootPath resolved to a blank path.");

            return resolved;
        }

        if (isDevelopment)
            return Path.GetFullPath(Path.Combine(contentRootPath, ".local-storage"));

        throw new InvalidOperationException(
            "FileStorage:RootPath must be configured outside Development (via the FileStorage__RootPath " +
            "environment variable or appsettings.Production.json). It must point to a persistent directory " +
            "outside the deployed application folder.");
    }

    /// <summary>
    /// Resolves the Public and Private sub-directories against an already-resolved absolute
    /// <paramref name="resolvedRoot"/> (from <see cref="ResolveRoot"/>). Both directory names
    /// must be relative (never an absolute path or a drive/UNC root) and must resolve to a
    /// location inside <paramref name="resolvedRoot"/> — this rejects a traversal value like
    /// "../../elsewhere" in FileStorage:PublicDirectory / FileStorage:PrivateDirectory.
    /// </summary>
    public static (string PublicRoot, string PrivateRoot) ResolveCategoryRoots(
        string resolvedRoot, string publicDirectory, string privateDirectory)
    {
        var publicRoot = ResolveSubDirectory(resolvedRoot, publicDirectory, "FileStorage:PublicDirectory");
        var privateRoot = ResolveSubDirectory(resolvedRoot, privateDirectory, "FileStorage:PrivateDirectory");
        return (publicRoot, privateRoot);
    }

    private static string ResolveSubDirectory(string resolvedRoot, string relativeDirectory, string settingName)
    {
        if (string.IsNullOrWhiteSpace(relativeDirectory))
            throw new InvalidOperationException($"{settingName} must not be blank.");

        if (Path.IsPathRooted(relativeDirectory))
            throw new InvalidOperationException($"{settingName} must be a relative directory name, not an absolute path.");

        var candidate = Path.GetFullPath(Path.Combine(resolvedRoot, relativeDirectory));

        if (!IsWithinRoot(candidate, resolvedRoot))
            throw new InvalidOperationException($"{settingName} must resolve to a location inside the configured storage root.");

        return candidate;
    }

    /// <summary>True if <paramref name="candidateFullPath"/> is <paramref name="rootFullPath"/> or nested under it.</summary>
    public static bool IsWithinRoot(string candidateFullPath, string rootFullPath) =>
        candidateFullPath.Equals(rootFullPath, StringComparison.OrdinalIgnoreCase)
        || candidateFullPath.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
