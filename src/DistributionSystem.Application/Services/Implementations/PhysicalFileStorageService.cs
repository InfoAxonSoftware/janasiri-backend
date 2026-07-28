using System.Text.RegularExpressions;
using DistributionSystem.Application.Configuration;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributionSystem.Application.Services.Implementations;

/// <summary>
/// Disk-backed <see cref="IFileStorageService"/>. Physical layout under the effective root:
/// {root}/public/{module}/{guid}{ext}  and  {root}/private/{module}/{guid}{ext}.
/// The effective root is never the deployed application's own folders (wwwroot/bin/obj/publish),
/// so runtime files survive redeploys. See <see cref="ResolveEffectiveRoot"/>.
/// </summary>
public sealed class PhysicalFileStorageService : IFileStorageService
{
    private const string PublicPrefix = "public/";
    private const string PrivatePrefix = "private/";

    // Never allow these regardless of a feature's own allowlist — defense in depth.
    private static readonly HashSet<string> DeniedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".bat", ".cmd", ".sh", ".ps1", ".msi", ".com", ".scr",
        ".vbs", ".js", ".jar", ".php", ".asp", ".aspx", ".cgi", ".jsp", ".htm", ".html",
    };

    private static readonly Regex ModuleSegmentPattern = new(@"^[a-zA-Z0-9][a-zA-Z0-9\-_]*$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> ContentTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".pdf"] = "application/pdf",
    };

    private readonly FileStorageOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PhysicalFileStorageService> _logger;
    private readonly string _effectiveRoot;
    private readonly string _publicRoot;
    private readonly string _privateRoot;
    private readonly string _legacyUploadsRoot;

    public PhysicalFileStorageService(
        IOptions<FileStorageOptions> options,
        IWebHostEnvironment env,
        ILogger<PhysicalFileStorageService> logger)
    {
        _options = options.Value;
        _env = env;
        _logger = logger;

        // Centralized resolution: a relative RootPath (e.g. MonsterASP's "../private") is always
        // combined against ContentRootPath and normalized to an absolute path here — the same
        // resolver Program.cs uses for the public PhysicalFileProvider and startup validation, so
        // every consumer agrees on the exact same physical location.
        _effectiveRoot = FileStorageRootResolver.ResolveRoot(_options.RootPath, env.ContentRootPath, env.IsDevelopment());
        (_publicRoot, _privateRoot) = FileStorageRootResolver.ResolveCategoryRoots(
            _effectiveRoot, _options.PublicDirectory, _options.PrivateDirectory);

        // Kept only to resolve records written before this abstraction existed; never written to.
        _legacyUploadsRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "wwwroot", "uploads"));

        Directory.CreateDirectory(_publicRoot);
        Directory.CreateDirectory(_privateRoot);

        _logger.LogInformation(
            "File storage initialized. Root={RootConfigured} PublicDir={PublicDir} PrivateDir={PrivateDir}",
            string.IsNullOrWhiteSpace(_options.RootPath) ? "(development fallback)" : "(configured)",
            _options.PublicDirectory, _options.PrivateDirectory);
    }

    public async Task<StoredFileResult> SaveAsync(
        IFormFile file,
        string module,
        FileAccessCategory category,
        IReadOnlyCollection<string> allowedExtensions,
        IReadOnlyCollection<string> allowedContentTypes,
        long? maxSizeBytesOverride = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ValidateModule(module);

        if (file.Length <= 0)
            throw new ArgumentException("File is empty.", nameof(file));

        var maxSize = maxSizeBytesOverride ?? _options.MaxFileSizeBytes;
        if (file.Length > maxSize)
            throw new ArgumentException($"File exceeds the {maxSize / (1024 * 1024)} MB limit.", nameof(file));

        var contentType = file.ContentType?.Trim().ToLowerInvariant() ?? "";
        if (allowedContentTypes.Count > 0 && !allowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Content type '{contentType}' is not allowed.", nameof(file));

        var ext = Path.GetExtension(file.FileName)?.Trim().ToLowerInvariant() ?? "";
        if (DeniedExtensions.Contains(ext))
            throw new ArgumentException($"File extension '{ext}' is not allowed.", nameof(file));
        if (ext.Length == 0 || !allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"File extension '{ext}' is not allowed.", nameof(file));

        var categoryRoot = category == FileAccessCategory.Public ? _publicRoot : _privateRoot;
        var moduleDir = Path.GetFullPath(Path.Combine(categoryRoot, module.Replace('/', Path.DirectorySeparatorChar)));
        EnsureWithinRoot(moduleDir, categoryRoot);
        Directory.CreateDirectory(moduleDir);

        var (fullPath, fileName) = await WriteWithCollisionRetryAsync(moduleDir, ext, file, ct);
        EnsureWithinRoot(fullPath, categoryRoot);

        var prefix = category == FileAccessCategory.Public ? PublicPrefix : PrivatePrefix;
        var storageKey = $"{prefix}{module}/{fileName}";

        _logger.LogInformation(
            "Saved file. Module={Module} Category={Category} SizeBytes={SizeBytes}",
            module, category, file.Length);

        return new StoredFileResult(storageKey, file.FileName, contentType, file.Length, category);
    }

    private static async Task<(string FullPath, string FileName)> WriteWithCollisionRetryAsync(
        string moduleDir, string ext, IFormFile file, CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(moduleDir, fileName);
            try
            {
                await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using var source = file.OpenReadStream();
                await source.CopyToAsync(stream, ct);
                return (fullPath, fileName);
            }
            catch (IOException) when (attempt < maxAttempts && File.Exists(fullPath))
            {
                // Filename collision on a fresh GUID is astronomically unlikely; retry with a new one.
            }
        }

        throw new IOException("Could not allocate a unique filename after multiple attempts.");
    }

    public async Task<FileReadResult?> ReadAsync(string? storageKeyOrLegacyValue, CancellationToken ct = default)
    {
        var physicalPath = ResolvePhysicalPath(storageKeyOrLegacyValue);
        if (physicalPath is null || !File.Exists(physicalPath))
            return null;

        await Task.CompletedTask; // resolution above is synchronous; kept async for interface symmetry/future backends
        var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return new FileReadResult
        {
            Content = stream,
            ContentType = ContentTypeFor(physicalPath),
            FileName = Path.GetFileName(physicalPath),
        };
    }

    public bool Exists(string? storageKeyOrLegacyValue)
    {
        var physicalPath = ResolvePhysicalPath(storageKeyOrLegacyValue);
        return physicalPath is not null && File.Exists(physicalPath);
    }

    public void Delete(string? storageKeyOrLegacyValue)
    {
        var physicalPath = ResolvePhysicalPath(storageKeyOrLegacyValue);
        if (physicalPath is null) return;

        try
        {
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);
        }
        catch (IOException ex)
        {
            // Best-effort cleanup: never let a delete failure (e.g. transient file lock) break the caller's flow.
            _logger.LogWarning(ex, "Failed to delete a stored file.");
        }
    }

    public string? GetPublicUrl(string? storageKeyOrLegacyValue)
    {
        if (string.IsNullOrWhiteSpace(storageKeyOrLegacyValue)) return null;

        if (storageKeyOrLegacyValue.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = storageKeyOrLegacyValue[PublicPrefix.Length..];
            return $"{_options.PublicRequestPath.TrimEnd('/')}/{remainder}";
        }

        if (storageKeyOrLegacyValue.StartsWith(PrivatePrefix, StringComparison.OrdinalIgnoreCase))
            return null; // private files are never exposed as a direct URL

        // Legacy public value already shaped like a servable URL (e.g. "/uploads/gallery/x.png").
        if (storageKeyOrLegacyValue.StartsWith('/'))
            return storageKeyOrLegacyValue;

        return null;
    }

    // ── Path resolution / validation ────────────────────────────────────────────────

    private string? ResolvePhysicalPath(string? storageKeyOrLegacyValue)
    {
        if (string.IsNullOrWhiteSpace(storageKeyOrLegacyValue)) return null;

        if (storageKeyOrLegacyValue.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveNewFormat(storageKeyOrLegacyValue[PublicPrefix.Length..], _publicRoot);

        if (storageKeyOrLegacyValue.StartsWith(PrivatePrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveNewFormat(storageKeyOrLegacyValue[PrivatePrefix.Length..], _privateRoot);

        return ResolveLegacyValue(storageKeyOrLegacyValue);
    }

    private string? ResolveNewFormat(string relative, string categoryRoot)
    {
        if (relative.Contains("..", StringComparison.Ordinal)) return null;

        var candidate = Path.GetFullPath(Path.Combine(categoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        return IsWithinRoot(candidate, categoryRoot) ? candidate : null;
    }

    /// <summary>
    /// Backward-compat resolution for values written before this abstraction existed: either a
    /// relative "/uploads/&lt;rest&gt;" URL, or (customer-registration documents only) a full
    /// absolute path that was stored directly in the database. Only ever reads from the legacy
    /// wwwroot/uploads location — never written to by new code.
    /// </summary>
    private string? ResolveLegacyValue(string legacyValue)
    {
        string? relative = null;

        if (legacyValue.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            relative = legacyValue["/uploads/".Length..];
        }
        else
        {
            var idx = legacyValue.IndexOf("uploads", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var afterUploads = legacyValue[(idx + "uploads".Length)..].TrimStart('/', '\\');
                relative = afterUploads.Replace('\\', '/');
            }
        }

        if (relative is null || relative.Contains("..", StringComparison.Ordinal))
            return null;

        var candidate = Path.GetFullPath(Path.Combine(_legacyUploadsRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        return IsWithinRoot(candidate, _legacyUploadsRoot) ? candidate : null;
    }

    private static bool IsWithinRoot(string candidateFullPath, string rootFullPath) =>
        FileStorageRootResolver.IsWithinRoot(candidateFullPath, rootFullPath);

    private static void EnsureWithinRoot(string candidateFullPath, string rootFullPath)
    {
        if (!IsWithinRoot(candidateFullPath, rootFullPath))
            throw new InvalidOperationException("Resolved storage path escaped the configured storage root.");
    }

    private static void ValidateModule(string module)
    {
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Module is required.", nameof(module));

        var segments = module.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Module is required.", nameof(module));

        foreach (var segment in segments)
        {
            if (!ModuleSegmentPattern.IsMatch(segment))
                throw new ArgumentException($"Invalid module segment '{segment}'.", nameof(module));
        }
    }

    private static string ContentTypeFor(string physicalPath)
    {
        var ext = Path.GetExtension(physicalPath);
        return ContentTypeByExtension.TryGetValue(ext, out var contentType) ? contentType : "application/octet-stream";
    }
}
