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
        Path.GetFileName(first.StorageKey).Should().NotBe("a.jpg"); // original filename is never reused
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

/// <summary>
/// Covers <see cref="FileStorageRootResolver"/> directly — this is the single place a relative
/// FileStorage:RootPath (e.g. MonsterASP's "../private", a sibling of the deployed wwwroot
/// folder) is combined against ContentRootPath and normalized to an absolute path, so both
/// PhysicalFileStorageService and Program.cs's public PhysicalFileProvider agree on the same
/// physical location instead of resolving it independently (the bug that previously caused
/// "The path must be absolute" at startup).
/// </summary>
public class FileStorageRootResolverTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "distsys-resolver-tests", Guid.NewGuid().ToString("N"), "wwwroot");

    public FileStorageRootResolverTests() => Directory.CreateDirectory(_contentRoot);

    public void Dispose()
    {
        var top = Path.GetDirectoryName(_contentRoot)!;
        if (Directory.Exists(top)) Directory.Delete(top, recursive: true);
    }

    [Fact]
    public void ResolveRoot_RelativeConfiguredRoot_ResolvesAgainstContentRootPath()
    {
        // Mirrors MonsterASP's layout: /wwwroot (ContentRootPath) with a sibling /private directory.
        var resolved = FileStorageRootResolver.ResolveRoot("../private", _contentRoot, isDevelopment: false);

        var expected = Path.GetFullPath(Path.Combine(_contentRoot, "../private"));
        resolved.Should().Be(expected);
        Path.IsPathRooted(resolved).Should().BeTrue();
        resolved.Should().NotContain("..", "the resolved path must be fully normalized, not just combined");
    }

    [Fact]
    public void ResolveRoot_DotLocalStorageConfiguredRoot_ResolvesAgainstContentRootPath()
    {
        var resolved = FileStorageRootResolver.ResolveRoot(".local-storage", _contentRoot, isDevelopment: false);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(_contentRoot, ".local-storage")));
        Path.IsPathRooted(resolved).Should().BeTrue();
    }

    [Fact]
    public void ResolveRoot_AbsoluteConfiguredRoot_RemainsValidAndIsNormalized()
    {
        var absoluteInput = Path.Combine(_contentRoot, "..", "private"); // absolute but not normalized
        Path.IsPathRooted(absoluteInput).Should().BeTrue();

        var resolved = FileStorageRootResolver.ResolveRoot(absoluteInput, _contentRoot, isDevelopment: false);

        resolved.Should().Be(Path.GetFullPath(absoluteInput));
        Path.IsPathRooted(resolved).Should().BeTrue();
    }

    [Fact]
    public void ResolveRoot_BlankOutsideDevelopment_ThrowsClearly()
    {
        var act = () => FileStorageRootResolver.ResolveRoot(null, _contentRoot, isDevelopment: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*FileStorage:RootPath*");
    }

    [Fact]
    public void ResolveRoot_BlankInDevelopment_FallsBackUnderContentRoot()
    {
        var resolved = FileStorageRootResolver.ResolveRoot(null, _contentRoot, isDevelopment: true);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(_contentRoot, ".local-storage")));
    }

    [Fact]
    public void ResolveCategoryRoots_PublicAndPrivate_RemainInsideStorageRoot()
    {
        var effectiveRoot = FileStorageRootResolver.ResolveRoot("../private", _contentRoot, isDevelopment: false);

        var (publicRoot, privateRoot) = FileStorageRootResolver.ResolveCategoryRoots(effectiveRoot, "public", "private");

        FileStorageRootResolver.IsWithinRoot(publicRoot, effectiveRoot).Should().BeTrue();
        FileStorageRootResolver.IsWithinRoot(privateRoot, effectiveRoot).Should().BeTrue();
        publicRoot.Should().NotBe(privateRoot);
    }

    [Theory]
    [InlineData("../elsewhere")]
    [InlineData("../../elsewhere")]
    [InlineData("public/../../elsewhere")]
    public void ResolveCategoryRoots_TraversalInPublicDirectory_IsRejected(string maliciousPublicDirectory)
    {
        var effectiveRoot = FileStorageRootResolver.ResolveRoot("../private", _contentRoot, isDevelopment: false);

        var act = () => FileStorageRootResolver.ResolveCategoryRoots(effectiveRoot, maliciousPublicDirectory, "private");

        act.Should().Throw<InvalidOperationException>().WithMessage("*PublicDirectory*");
    }

    [Theory]
    [InlineData("../elsewhere")]
    [InlineData("../../elsewhere")]
    public void ResolveCategoryRoots_TraversalInPrivateDirectory_IsRejected(string maliciousPrivateDirectory)
    {
        var effectiveRoot = FileStorageRootResolver.ResolveRoot("../private", _contentRoot, isDevelopment: false);

        var act = () => FileStorageRootResolver.ResolveCategoryRoots(effectiveRoot, "public", maliciousPrivateDirectory);

        act.Should().Throw<InvalidOperationException>().WithMessage("*PrivateDirectory*");
    }

    [Fact]
    public void ResolveCategoryRoots_AbsolutePublicDirectory_IsRejected()
    {
        var effectiveRoot = FileStorageRootResolver.ResolveRoot("../private", _contentRoot, isDevelopment: false);
        var absoluteDirectory = OperatingSystem.IsWindows() ? @"C:\somewhere-else" : "/somewhere-else";

        var act = () => FileStorageRootResolver.ResolveCategoryRoots(effectiveRoot, absoluteDirectory, "private");

        act.Should().Throw<InvalidOperationException>().WithMessage("*PublicDirectory*");
    }

    [Fact]
    public void ResolveCategoryRoots_BlankDirectoryName_IsRejected()
    {
        var effectiveRoot = FileStorageRootResolver.ResolveRoot("../private", _contentRoot, isDevelopment: false);

        var act = () => FileStorageRootResolver.ResolveCategoryRoots(effectiveRoot, "  ", "private");

        act.Should().Throw<InvalidOperationException>().WithMessage("*PublicDirectory*");
    }

    [Fact]
    public void ResolveRoot_RelativeRootPath_ProducesAbsolutePathAcceptedByPhysicalFileProvider()
    {
        // Regression test for the exact production failure: PhysicalFileProvider throws
        // ArgumentException ("The path must be absolute") when handed an un-resolved relative
        // path. FileStorage__RootPath=../private (MonsterASP) must never reach it un-resolved.
        var effectiveRoot = FileStorageRootResolver.ResolveRoot("../private", _contentRoot, isDevelopment: false);
        var (publicRoot, _) = FileStorageRootResolver.ResolveCategoryRoots(effectiveRoot, "public", "private");
        Directory.CreateDirectory(publicRoot);

        Path.IsPathRooted(publicRoot).Should().BeTrue();
        var act = () => new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicRoot);

        act.Should().NotThrow();
    }
}
