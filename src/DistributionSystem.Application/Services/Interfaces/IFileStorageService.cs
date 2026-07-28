using Microsoft.AspNetCore.Http;

namespace DistributionSystem.Application.Services.Interfaces;

public enum FileAccessCategory
{
    Public,
    Private,
}

/// <summary>
/// Outcome of a successful save. <see cref="StorageKey"/> is the only value business services
/// should persist to the database — never a physical path.
/// </summary>
public sealed record StoredFileResult(
    string StorageKey,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    FileAccessCategory Category);

/// <summary>An open, readable file stream plus the metadata needed to serve it over HTTP.</summary>
public sealed class FileReadResult : IDisposable
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }

    public void Dispose() => Content.Dispose();
}

/// <summary>
/// Storage abstraction used by every feature that saves, reads or deletes runtime files
/// (customer KYC documents, payment evidence, quick-request attachments, gallery/product images).
/// Business/application services depend only on this interface — never on IWebHostEnvironment,
/// WebRootPath, or a physical wwwroot path — so the implementation can later be swapped for
/// object storage (S3/Azure Blob/R2/Supabase) without touching callers.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Validates and saves <paramref name="file"/> under the given <paramref name="module"/>
    /// (a short logical folder name/path, e.g. "rep-payments" or "customer-registrations/{id}").
    /// The stored filename is always freshly generated (GUID) — the browser-supplied filename is
    /// never trusted beyond extracting and validating its extension.
    /// </summary>
    Task<StoredFileResult> SaveAsync(
        IFormFile file,
        string module,
        FileAccessCategory category,
        IReadOnlyCollection<string> allowedExtensions,
        IReadOnlyCollection<string> allowedContentTypes,
        long? maxSizeBytesOverride = null,
        CancellationToken ct = default);

    /// <summary>
    /// Opens a file for reading by its storage key (new format: "public/..." or "private/...")
    /// or a legacy stored value (old "/uploads/..." URL or absolute pre-migration path) for
    /// backward compatibility with records written before this abstraction existed.
    /// Returns null if the key is empty/unresolvable or the file no longer exists.
    /// </summary>
    Task<FileReadResult?> ReadAsync(string? storageKeyOrLegacyValue, CancellationToken ct = default);

    /// <summary>Same key resolution as <see cref="ReadAsync"/>, without opening the file.</summary>
    bool Exists(string? storageKeyOrLegacyValue);

    /// <summary>Deletes the file if it exists. No-op (never throws) if the key is empty or unresolvable.</summary>
    void Delete(string? storageKeyOrLegacyValue);

    /// <summary>
    /// Builds the publicly-servable URL for a Public-category storage key (new "public/..." key,
    /// or an already-public-shaped legacy "/uploads/..." value passed through unchanged).
    /// Returns null for Private-category keys — those must go through an authenticated endpoint.
    /// </summary>
    string? GetPublicUrl(string? storageKeyOrLegacyValue);
}
