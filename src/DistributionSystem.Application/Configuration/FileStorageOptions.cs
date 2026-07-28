namespace DistributionSystem.Application.Configuration;

/// <summary>
/// Non-secret configuration for the runtime file storage root. Bound from the "FileStorage"
/// configuration section (appsettings.{Environment}.json or FileStorage__* environment variables).
/// </summary>
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Physical root directory for all runtime-uploaded/generated files. Must never be a path
    /// under the deployed application (bin/obj/publish/wwwroot), since deployments can replace
    /// or delete those. Required in Production; may be left blank in Development to use a local,
    /// non-machine-specific fallback outside the publish output.
    /// </summary>
    public string RootPath { get; set; } = "";

    /// <summary>Sub-directory of RootPath holding files servable to anyone (e.g. gallery/product images).</summary>
    public string PublicDirectory { get; set; } = "public";

    /// <summary>Sub-directory of RootPath holding files that require authenticated, authorized access.</summary>
    public string PrivateDirectory { get; set; } = "private";

    /// <summary>URL path prefix the public directory is mapped to for static serving.</summary>
    public string PublicRequestPath { get; set; } = "/uploads";

    /// <summary>Default max upload size in bytes, used when a feature doesn't specify its own limit.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}
