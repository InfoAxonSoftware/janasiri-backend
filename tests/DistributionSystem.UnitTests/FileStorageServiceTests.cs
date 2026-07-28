using DistributionSystem.Application.Configuration;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DistributionSystem.UnitTests;

internal sealed class FakeStorageWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
}

internal static class TestFormFile
{
    public static IFormFile Create(string fileName, string contentType, int sizeBytes = 16)
    {
        var bytes = new byte[sizeBytes];
        Random.Shared.NextBytes(bytes);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName) { Headers = new HeaderDictionary(), ContentType = contentType };
    }
}

/// <summary>
/// Exercises PhysicalFileStorageService in isolation against a temp directory that's deleted
/// after each test — no real (and definitely no customer) files are ever touched.
/// </summary>
public class FileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "distsys-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly string _legacyContentRoot = Path.Combine(Path.GetTempPath(), "distsys-storage-tests-legacy", Guid.NewGuid().ToString("N"));

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];
    private static readonly string[] ImageContentTypes = ["image/jpeg", "image/png"];

    private IFileStorageService CreateService(string? rootPath, bool isDevelopment = false)
    {
        var env = new FakeStorageWebHostEnvironment
        {
            EnvironmentName = isDevelopment ? "Development" : "Production",
            ContentRootPath = _legacyContentRoot,
        };
        var options = Options.Create(new FileStorageOptions { RootPath = rootPath ?? "" });
        return new PhysicalFileStorageService(options, env, NullLogger<PhysicalFileStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        if (Directory.Exists(_legacyContentRoot)) Directory.Delete(_legacyContentRoot, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_ValidFile_ReturnsStorageKeyAndPersistsContent()
    {
        var service = CreateService(_root);
        var file = TestFormFile.Create("photo.jpg", "image/jpeg");

        var result = await service.SaveAsync(file, "rep-payments", FileAccessCategory.Private, ImageExtensions, ImageContentTypes);

        result.StorageKey.Should().StartWith("private/rep-payments/");
        result.StorageKey.Should().EndWith(".jpg");
        service.Exists(result.StorageKey).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_StorageKey_NeverContainsAnAbsolutePath()
    {
        var service = CreateService(_root);
        var file = TestFormFile.Create("photo.jpg", "image/jpeg");

        var result = await service.SaveAsync(file, "gallery", FileAccessCategory.Public, ImageExtensions, ImageContentTypes);

        result.StorageKey.Should().NotContain(":\\");
        result.StorageKey.Should().NotContain(_root);
        Path.IsPathRooted(result.StorageKey).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_DisallowedExtension_Throws()
    {
        var service = CreateService(_root);
        var file = TestFormFile.Create("malware.exe", "application/octet-stream");

        var act = async () => await service.SaveAsync(file, "gallery", FileAccessCategory.Public, ImageExtensions, ImageContentTypes);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveAsync_ExtensionNotInAllowlist_ThrowsEvenIfNotExplicitlyDenied()
    {
        var service = CreateService(_root);
        // .txt isn't in the deny-list, but it's also not in this feature's allowlist.
        var file = TestFormFile.Create("notes.txt", "text/plain");

        var act = async () => await service.SaveAsync(file, "gallery", FileAccessCategory.Public, ImageExtensions, ImageContentTypes);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveAsync_OversizedFile_Throws()
    {
        var service = CreateService(_root);
        var file = TestFormFile.Create("big.jpg", "image/jpeg", sizeBytes: 2048);

        var act = async () => await service.SaveAsync(file, "gallery", FileAccessCategory.Public, ImageExtensions, ImageContentTypes, maxSizeBytesOverride: 1024);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveAsync_TwoUploads_GetDifferentRandomFilenames()
    {
        var service = CreateService(_root);

        var first = await service.SaveAsync(TestFormFile.Create("a.jpg", "image/jpeg"), "gallery", FileAccessCategory.Public, ImageExtensions, ImageContentTypes);
        var second = await service.SaveAsync(TestFormFile.Create("a.jpg", "image/jpeg"), "gallery", FileAccessCategory.Public, ImageExtensions, ImageContentTypes);

        first.StorageKey.Should().NotBe(second.StorageKey);
        first.StorageKey.Should().NotContain("a.jpg"); // original filename is never trusted/reused
    }

    [Theory]
    [InlineData("private/../../../etc/passwd")]
    [InlineData("public/../../secrets/config.json")]
    [InlineData("private/rep-payments/../../../windows/win.ini")]
    public void ResolvePhysicalPath_PathTraversalAttempt_IsRejected(string maliciousKey)
    {
        var service = CreateService(_root);

        service.Exists(maliciousKey).Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_PrivateFile_IsNotExposedAsAPublicUrl()
    {
        var service = CreateService(_root);
        var saved = await service.SaveAsync(TestFormFile.Create("evidence.png", "image/png"), "rep-payments", FileAccessCategory.Private, ImageExtensions, ImageContentTypes);

        service.GetPublicUrl(saved.StorageKey).Should().BeNull();

        using var read = await service.ReadAsync(saved.StorageKey);
        read.Should().NotBeNull(); // still readable directly by key — that's how the authenticated endpoint serves it
    }

    [Fact]
    public async Task GetPublicUrl_PublicFile_BuildsUrlUnderConfiguredRequestPath()
    {
        var service = CreateService(_root);
        var saved = await service.SaveAsync(TestFormFile.Create("logo.png", "image/png"), "gallery", FileAccessCategory.Public, ImageExtensions, ImageContentTypes);

        var url = service.GetPublicUrl(saved.StorageKey);

        url.Should().StartWith("/uploads/gallery/");
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var service = CreateService(_root);
        var saved = await service.SaveAsync(TestFormFile.Create("temp.png", "image/png"), "quick-requests", FileAccessCategory.Private, ImageExtensions, ImageContentTypes);

        service.Exists(saved.StorageKey).Should().BeTrue();
        service.Delete(saved.StorageKey);
        service.Exists(saved.StorageKey).Should().BeFalse();
    }

    [Fact]
    public void Delete_UnknownOrNullKey_DoesNotThrow()
    {
        var service = CreateService(_root);

        var act1 = () => service.Delete(null);
        var act2 = () => service.Delete("private/does-not-exist/x.png");

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public async Task Replacement_OldFileDeletedOnlyAfterNewFileSaved()
    {
        var service = CreateService(_root);
        var original = await service.SaveAsync(TestFormFile.Create("v1.png", "image/png"), "customer-registrations/abc", FileAccessCategory.Private, ImageExtensions, ImageContentTypes);

        var replacement = await service.SaveAsync(TestFormFile.Create("v2.png", "image/png"), "customer-registrations/abc", FileAccessCategory.Private, ImageExtensions, ImageContentTypes);
        // Caller pattern: only delete the old key once the new one is confirmed saved (mirrors CustomerService.UpdateRegistrationDetailsAsync).
        service.Exists(original.StorageKey).Should().BeTrue("the old file must still exist until the caller explicitly deletes it");
        service.Delete(original.StorageKey);

        service.Exists(original.StorageKey).Should().BeFalse();
        service.Exists(replacement.StorageKey).Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_LegacyRelativeUploadsValue_ResolvesUnderLegacyRoot()
    {
        var service = CreateService(_root);
        var legacyDir = Path.Combine(_legacyContentRoot, "wwwroot", "uploads", "gallery");
        Directory.CreateDirectory(legacyDir);
        var legacyFile = Path.Combine(legacyDir, "old-logo.png");
        await File.WriteAllBytesAsync(legacyFile, [1, 2, 3, 4]);

        using var read = await service.ReadAsync("/uploads/gallery/old-logo.png");

        read.Should().NotBeNull();
        read!.FileName.Should().Be("old-logo.png");
    }

    [Fact]
    public void ResolveEffectiveRoot_MissingRootPathOutsideDevelopment_ThrowsClearly()
    {
        var act = () => CreateService(rootPath: null, isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FileStorage:RootPath*");
    }

    [Fact]
    public void ResolveEffectiveRoot_MissingRootPathInDevelopment_FallsBackWithoutThrowing()
    {
        var act = () => CreateService(rootPath: null, isDevelopment: true);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ReadAsync_UnresolvableOrMissingKey_ReturnsNull()
    {
        var service = CreateService(_root);

        (await service.ReadAsync(null)).Should().BeNull();
        (await service.ReadAsync("")).Should().BeNull();
        (await service.ReadAsync("private/gallery/does-not-exist.png")).Should().BeNull();
    }
}
