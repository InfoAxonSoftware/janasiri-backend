using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.HttpOverrides;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Application.Validators;
using DistributionSystem.API.Hubs;
using DistributionSystem.API.Middleware;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using DistributionSystem.Infrastructure.Identity;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using DistributionSystem.API.Services;

// Enable legacy timestamp behavior for Npgsql (DateTime with Kind=Utc)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ===== Serilog =====
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ===== Database =====
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection must be configured (via User Secrets locally, " +
        "or the ConnectionStrings__DefaultConnection environment variable in production).");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// ===== File Storage =====
// Runtime files (KYC documents, payment evidence, gallery/product images, ...) must live outside
// the deployed application folder so redeploys never touch them. FileStorage:RootPath is required
// in every environment except Development (see PhysicalFileStorageService.ResolveEffectiveRoot for
// the Development fallback). Fail fast and clearly here rather than on first upload.
builder.Services.Configure<DistributionSystem.Application.Configuration.FileStorageOptions>(
    builder.Configuration.GetSection(DistributionSystem.Application.Configuration.FileStorageOptions.SectionName));
if (!builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(builder.Configuration[$"{DistributionSystem.Application.Configuration.FileStorageOptions.SectionName}:RootPath"]))
{
    throw new InvalidOperationException(
        "FileStorage:RootPath must be configured outside Development (via the FileStorage__RootPath " +
        "environment variable or appsettings.Production.json). It must point to a persistent directory " +
        "outside the deployed application folder.");
}
builder.Services.AddSingleton<IFileStorageService, PhysicalFileStorageService>();

// ===== Repositories =====
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ===== Application Services =====
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IRepService, RepService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationPublisher, NotificationPublisher>();
builder.Services.AddScoped<ISupportService, SupportService>();
builder.Services.AddScoped<ISupportChatPublisher, SupportChatPublisher>();

// background job for generating visits from routes
builder.Services.AddHostedService<DistributionSystem.Application.Services.Background.DailyVisitGenerator>();
// background job for purging soft-deleted records (30-day trash cleanup)
builder.Services.AddHostedService<DistributionSystem.Application.Services.Background.TrashCleanupService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ICoordinatorService, CoordinatorService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<ISystemConfigService, SystemConfigService>();
builder.Services.AddScoped<ICustomerRegistrationService, CustomerRegistrationService>();
builder.Services.AddScoped<IRegionService, RegionService>();
builder.Services.AddScoped<IGalleryService, GalleryService>();
builder.Services.AddScoped<IOutstandingReportService, OutstandingReportService>();
builder.Services.AddScoped<IStockReportService, StockReportService>();
builder.Services.AddScoped<ISalesSummaryReportService, SalesSummaryReportService>();
builder.Services.AddScoped<ITargetReportService, TargetReportService>();
builder.Services.AddScoped<IQuickRequestService, QuickRequestService>();
builder.Services.AddScoped<IRepPaymentService, RepPaymentService>();

// ===== Infrastructure Services =====
builder.Services.AddScoped<JwtService>();

// ===== FluentValidation =====
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductValidator>();

// ===== JWT Authentication =====
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Secret"];
if (string.IsNullOrWhiteSpace(secretKey))
{
    throw new InvalidOperationException(
        "JwtSettings:Secret must be configured (via User Secrets locally, " +
        "or the JwtSettings__Secret environment variable in production).");
}
if (Encoding.UTF8.GetByteCount(secretKey) < 32)
{
    // HMAC-SHA256 signing requires a key of at least 256 bits (32 bytes); a shorter key is
    // cryptographically weak and must never be silently accepted, especially in production.
    throw new InvalidOperationException(
        "JwtSettings:Secret is too short. It must be at least 32 bytes (256 bits) to safely sign tokens with HMAC-SHA256.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        NameClaimType = ClaimTypes.NameIdentifier,
        RoleClaimType = ClaimTypes.Role,
        ValidIssuer = jwtSettings["Issuer"]
            ?? throw new InvalidOperationException("JwtSettings:Issuer must be configured."),
        ValidAudience = jwtSettings["Audience"]
            ?? throw new InvalidOperationException("JwtSettings:Audience must be configured."),
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };

    // Allow SignalR to receive token from query string and enforce user token version/password change policies
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var userIdValue = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdValue) || !Guid.TryParse(userIdValue, out var userId))
            {
                context.Fail("Invalid user identifier");
                return;
            }

            var tokenVersionClaim = context.Principal?.FindFirst("tokenVersion")?.Value;
            var passwordChangedAtClaim = context.Principal?.FindFirst("passwordChangedAt")?.Value;

            var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
            var user = await unitOfWork.Repository<DistributionSystem.Domain.Entities.User>().GetByIdAsync(userId, context.HttpContext.RequestAborted);
            if (user == null || !user.IsActive)
            {
                context.Fail("User not found or inactive");
                return;
            }

            // NOTE: Auto invalidation on tokenVersion/passwordChangedAt is temporarily disabled to avoid immediate logout after admin-generated temp-reset.
            // Keep the user as active and valid, but still check account status.
            // This means tokens remain valid until explicit log out or expiration.

            // if (!int.TryParse(tokenVersionClaim, out var tokenVersion) || tokenVersion != user.TokenVersion)
            // {
            //     context.Fail("Token has been revoked by admin or user password reset");
            //     return;
            // }

            // if (user.PasswordChangedAt.HasValue)
            // {
            //     if (string.IsNullOrEmpty(passwordChangedAtClaim) ||
            //         !DateTime.TryParse(passwordChangedAtClaim, null, System.Globalization.DateTimeStyles.RoundtripKind, out var tokenPasswordChangedAt) ||
            //         tokenPasswordChangedAt.ToUniversalTime() < user.PasswordChangedAt.Value.ToUniversalTime())
            //     {
            //         context.Fail("Expired due to password change");
            //         return;
            //     }
            // }
        }
    };
});

builder.Services.AddAuthorization();

// ===== Controllers =====
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        // allow enums to be serialized/deserialized as their string names instead of numeric values
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// ===== SignalR =====
builder.Services.AddSignalR();

// ===== CORS =====
// Allowed origins come from configuration (CorsOrigins section: appsettings.{Environment}.json,
// User Secrets, or the CorsOrigins__0 / CorsOrigins__1 / ... environment variables) rather than
// being hardcoded here, so each environment supplies its own value without code changes.
var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? [];
if (!builder.Environment.IsDevelopment() && corsOrigins.Length == 0)
{
    // Fail-safe, not fail-fast: WithOrigins([]) below allows no cross-origin requests at all,
    // so the app still starts (e.g. API-only/health-check use), but every browser client is
    // blocked by CORS until an origin is configured. Surface that loudly instead of silently.
    Log.Warning("No CorsOrigins are configured for the {Environment} environment. " +
                "All cross-origin browser requests will be blocked until CorsOrigins is set.",
                builder.Environment.EnvironmentName);
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()
                  .SetIsOriginAllowed(_ => true);
        }
        else
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// ===== Swagger / OpenAPI =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var swaggerContact = builder.Configuration.GetSection("SwaggerContact");
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Distribution Management System API",
        Version = "v1",
        Description = "B2B Wholesale Distribution Management System - REST API with JWT Authentication",
        Contact = new OpenApiContact
        {
            Name = swaggerContact["Name"] ?? "Support",
            Email = swaggerContact["Email"] ?? ""
        }
    });

    // JWT Bearer token support in Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Include XML comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// ===== Response Compression =====
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// run a one‑time schema fix on startup to create missing order‑item snapshot columns
builder.Services.AddHostedService<EnsureSnapshotColumnsService>();

// ===== Health Checks =====
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

var app = builder.Build();

// ===== Forwarded Headers (must be FIRST) =====
// runasp.net and similar IIS-hosted environments terminate SSL at the proxy,
// so Request.Scheme arrives as "http" inside the app. Trusting X-Forwarded-Proto
// makes Request.Scheme correctly report "https" for URL generation.
var fwdOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
};
fwdOptions.KnownIPNetworks.Clear(); // trust all upstream proxies (safe for single-server deployments)
fwdOptions.KnownProxies.Clear();
app.UseForwardedHeaders(fwdOptions);

app.UseRouting();

app.UseCors("AllowFrontend");

// ===== Middleware Pipeline =====
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Distribution System API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseSerilogRequestLogging();

// ===== Public file serving =====
// Only the configured PUBLIC storage directory is ever mounted as static files (gallery/product
// images). Private files (KYC documents, payment evidence, quick-request attachments) are never
// reachable this way — they're served exclusively through authenticated, ownership-checked API
// endpoints (see CustomerController/RepPaymentController/QuickRequestController). Forcing service
// resolution here also creates the storage directories up front.
var fileStorageOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DistributionSystem.Application.Configuration.FileStorageOptions>>().Value;
_ = app.Services.GetRequiredService<IFileStorageService>();

var publicStorageRoot = string.IsNullOrWhiteSpace(fileStorageOptions.RootPath)
    ? Path.Combine(app.Environment.ContentRootPath, ".local-storage", fileStorageOptions.PublicDirectory)
    : Path.Combine(fileStorageOptions.RootPath, fileStorageOptions.PublicDirectory);

void ReflectCorsOrigin(Microsoft.AspNetCore.StaticFiles.StaticFileResponseContext ctx)
{
    // Allow cross-origin fetch()/<img> for public assets (e.g. frontend on a different origin).
    var origin = ctx.Context.Request.Headers["Origin"].ToString();
    if (!string.IsNullOrEmpty(origin))
    {
        ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        ctx.Context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        ctx.Context.Response.Headers["Vary"] = "Origin";
    }
}

app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicStorageRoot),
    RequestPath = fileStorageOptions.PublicRequestPath,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ReflectCorsOrigin,
});

// Backward-compat fallback: public images (gallery/product) saved before this abstraction existed
// still physically live under the old wwwroot/uploads folder. StaticFileMiddleware passes through
// to the next middleware when a file isn't found in its own root, so chaining a second instance at
// the same request path serves old files without ever writing new ones there. Only the known-public
// legacy subfolders are mounted this way — customer-registrations/rep-payments/quick-requests must
// NEVER be reachable anonymously, so they are deliberately excluded here.
foreach (var legacyPublicModule in new[] { "gallery", "products" })
{
    var legacyModulePath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads", legacyPublicModule);
    Directory.CreateDirectory(legacyModulePath);
    app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(legacyModulePath),
        RequestPath = $"{fileStorageOptions.PublicRequestPath.TrimEnd('/')}/{legacyPublicModule}",
        ServeUnknownFileTypes = false,
        OnPrepareResponse = ReflectCorsOrigin,
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ===== SignalR Hubs =====
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<OrderTrackingHub>("/hubs/order-tracking");
app.MapHub<SupportHub>("/hubs/support");

// ===== Health Check endpoint =====
app.MapHealthChecks("/health");

// ===== Auto-migrate & seed on startup (configurable) =====
var autoMigrateOnStartup = builder.Configuration.GetValue("DatabaseSettings:AutoMigrateOnStartup", app.Environment.IsDevelopment());
var seedDataOnStartup = builder.Configuration.GetValue("DatabaseSettings:SeedDataOnStartup", app.Environment.IsDevelopment());
var seedSuperAdminConfigured = builder.Configuration.GetValue("DatabaseSettings:SeedSuperAdminOnStartup", app.Environment.IsProduction());
var seedSuperAdminOnStartup = app.Environment.IsProduction() && seedSuperAdminConfigured;

var superAdminUsername = builder.Configuration["SuperAdminSeed:Username"];
var superAdminEmail = builder.Configuration["SuperAdminSeed:Email"];
var superAdminPassword = builder.Configuration["SuperAdminSeed:Password"];
var superAdminPhoneNumber = builder.Configuration["SuperAdminSeed:PhoneNumber"];
var superAdminFullName = builder.Configuration["SuperAdminSeed:FullName"];
var superAdminDepartment = builder.Configuration["SuperAdminSeed:Department"];

if (seedSuperAdminOnStartup)
{
    var missingConfig = new List<string>();
    if (string.IsNullOrWhiteSpace(superAdminUsername)) missingConfig.Add("SuperAdminSeed:Username");
    if (string.IsNullOrWhiteSpace(superAdminEmail)) missingConfig.Add("SuperAdminSeed:Email");
    if (string.IsNullOrWhiteSpace(superAdminPassword)) missingConfig.Add("SuperAdminSeed:Password");
    if (string.IsNullOrWhiteSpace(superAdminPhoneNumber)) missingConfig.Add("SuperAdminSeed:PhoneNumber");
    if (string.IsNullOrWhiteSpace(superAdminFullName)) missingConfig.Add("SuperAdminSeed:FullName");
    if (string.IsNullOrWhiteSpace(superAdminDepartment)) missingConfig.Add("SuperAdminSeed:Department");

    if (missingConfig.Count > 0)
    {
        throw new InvalidOperationException($"SeedSuperAdminOnStartup is enabled, but required environment settings are missing: {string.Join(", ", missingConfig)}");
    }
}

if (autoMigrateOnStartup || seedDataOnStartup || seedSuperAdminOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    if (autoMigrateOnStartup)
        await db.Database.MigrateAsync();

    if (seedDataOnStartup)
        await DistributionSystem.Infrastructure.Data.SeedData.InitializeAsync(db, superAdminPassword);

    if (seedSuperAdminOnStartup)
        await DistributionSystem.Infrastructure.Data.SeedData.EnsureSuperAdminAsync(
            db,
            superAdminUsername!,
            superAdminEmail!,
            superAdminPassword!,
            superAdminPhoneNumber!,
            superAdminFullName!,
            superAdminDepartment!);
}

Log.Information("Distribution Management System API started successfully");

app.Run();

public partial class Program { }
