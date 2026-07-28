# Backend Configuration Setup

This document describes how configuration and secrets are structured for the
`DistributionSystem.API` backend, how to set up local development, and how
production values are supplied. **No real secret values appear in this file
or anywhere in source control — placeholders only.**

## 1. Configuration hierarchy

ASP.NET Core loads configuration in this order; later sources override earlier ones:

1. `appsettings.json` — shared, non-secret defaults (committed).
2. `appsettings.{ASPNETCORE_ENVIRONMENT}.json` — e.g. `appsettings.Development.json`
   or `appsettings.Production.json` — environment-specific, non-secret values (committed).
3. **.NET User Secrets** — local-development-only secrets, stored outside the repo
   (Development environment only; never used in production).
4. **Environment variables** — the standard production secret/override mechanism,
   using the ASP.NET Core `__` (double-underscore) nesting syntax.
5. Command-line arguments (not used in this project's deployment).

`appsettings.Development.json` is excluded from `dotnet publish` output (see
`DistributionSystem.API.csproj`), so it never reaches a deployed environment.

## 2. Required configuration keys

| Key | Secret? | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | **Yes** | PostgreSQL connection string. Startup throws if missing. |
| `JwtSettings:Secret` | **Yes** | HMAC-SHA256 signing key. Must be ≥ 32 bytes (256 bits). Startup throws if missing or too short. |
| `JwtSettings:Issuer` | No | Token issuer, e.g. `DistributionSystem`. |
| `JwtSettings:Audience` | No | Token audience, e.g. `DistributionSystemClients`. |
| `JwtSettings:AccessTokenExpirationMinutes` | No | Access token lifetime. |
| `JwtSettings:RefreshTokenExpirationDays` | No | Refresh token lifetime. |
| `SmtpSettings:Enabled` | No | Master on/off switch for outbound email. Defaults to `false` (disabled) when absent. |
| `SmtpSettings:Host` | No | SMTP server host. |
| `SmtpSettings:Port` | No | SMTP server port. |
| `SmtpSettings:Username` | **Yes** | SMTP auth username. |
| `SmtpSettings:Password` | **Yes** | SMTP auth password. |
| `SmtpSettings:FromEmail` / `FromName` / `AdminEmail` / `EnableSsl` | No | Non-secret mail settings. |
| `SecuritySettings:MaxFailedLoginAttempts` / `LockoutDurationMinutes` | No | Login lockout policy. |
| `DatabaseSettings:AutoMigrateOnStartup` | No | Should stay `false` in production unless a controlled migration run is intended. |
| `DatabaseSettings:SeedDataOnStartup` | No | Demo/sample data seeding — keep `false` in production. |
| `DatabaseSettings:SeedSuperAdminOnStartup` | No | Gate for the one-time super-admin seed described below. |
| `SuperAdminSeed:Username` / `Email` / `PhoneNumber` / `FullName` / `Department` | No | Only read when `SeedSuperAdminOnStartup` is `true`. |
| `SuperAdminSeed:Password` | **Yes** | Only read when `SeedSuperAdminOnStartup` is `true`. |
| `DefaultSettings:*` | No | Business defaults (credit limit, report range, etc.). |
| `SwaggerContact:Name` / `Email` | No | Swagger UI contact info. |
| `CorsOrigins` (array) | No | Allowed browser origins for the `AllowFrontend` CORS policy. Empty in production = all cross-origin requests blocked (fail-safe). |
| `AllowedHosts` | No | ASP.NET Core host filtering. |

## 3. Local development: .NET User Secrets

Run these from `src/DistributionSystem.API/` (the `UserSecretsId` is already
set in the `.csproj`). **Replace every placeholder with your own local value —
never a production credential.**

```bash
cd src/DistributionSystem.API

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=DistributionDB;Username=<local-user>;Password=<local-password>"
dotnet user-secrets set "JwtSettings:Secret" "<local-dev-secret-at-least-32-bytes>"
dotnet user-secrets set "SmtpSettings:Username" "<local-smtp-username-or-blank>"
dotnet user-secrets set "SmtpSettings:Password" "<local-smtp-password-or-blank>"
dotnet user-secrets set "SuperAdminSeed:Password" "<local-super-admin-password>"
```

List / verify what's set (values are shown locally only — never commit this output):

```bash
dotnet user-secrets list
```

Remove a value:

```bash
dotnet user-secrets remove "JwtSettings:Secret"
```

## 4. Production: environment variables

MonsterASP (or any other host) must supply secrets via environment variables
using the ASP.NET Core double-underscore convention:

```
ConnectionStrings__DefaultConnection
JwtSettings__Secret
SmtpSettings__Username
SmtpSettings__Password
SuperAdminSeed__Password   (only if SeedSuperAdminOnStartup=true)
CorsOrigins__0             (first allowed origin; __1, __2, ... for more)
```

Non-secret production values (log level, feature flags, non-secret CORS
origins) can instead live in `appsettings.Production.json`, which is committed
and contains no secrets.

## 5. Development start commands

```bash
cd src/DistributionSystem.API
dotnet user-secrets list   # confirm required secrets are set (see section 3)
dotnet run
```

`launchSettings.json` already sets `ASPNETCORE_ENVIRONMENT=Development` for
the `http`/`https` profiles.

## 6. Production publish command

```bash
dotnet publish src/DistributionSystem.API/DistributionSystem.API.csproj -c Release -o ./publish_output --runtime win-x86 --self-contained false
```

(This matches the existing `.github/workflows/deploy.yml` FTP deployment to MonsterASP.)

## 7. MonsterASP items that still require confirmation

- **Production frontend origin(s)** — `appsettings.Production.json` currently lists
  `https://janasiridistribution.vercel.app`, carried over from the existing
  `CorsOrigins` values already present in the repo. Confirm this is still the
  correct production frontend before relying on it, and add any additional
  origins (custom domain, www subdomain, etc.) via `CorsOrigins__1`, `CorsOrigins__2`, ...
- **FTP host/username** in `.github/workflows/deploy.yml` (`site60153.siteasp.net` /
  `site60153`) — confirm these are still the correct MonsterASP site credentials;
  `FTP_PASSWORD` is already supplied via a GitHub Actions secret, not hardcoded.
- **Actual production PostgreSQL connection string** — must be set as
  `ConnectionStrings__DefaultConnection` in the MonsterASP application settings
  (or equivalent). Not present anywhere in this repo.
- **Actual production JWT signing secret** — must be set as `JwtSettings__Secret`
  in the MonsterASP application settings. Generate a new value; do not reuse any
  development secret.
- **Whether `DatabaseSettings:AutoMigrateOnStartup` / `SeedSuperAdminOnStartup`
  should run on first production deploy** — currently defaults to `false` in
  `appsettings.Production.json`; this phase does not change that behavior.

## 8. How to verify the active ASP.NET Core environment

```bash
# Locally:
echo $ASPNETCORE_ENVIRONMENT      # bash
echo $env:ASPNETCORE_ENVIRONMENT  # PowerShell

# At runtime, call the health endpoint and check logs on startup —
# Program.cs logs "Distribution Management System API started successfully"
# after all environment-gated startup logic (migrate/seed) has evaluated.
```

On MonsterASP/IIS, `ASPNETCORE_ENVIRONMENT` is set to `Production` in `web.config`
under `<aspNetCore><environmentVariables>`.

## 9. How to verify configuration without printing secrets

```bash
# Confirm a key IS set, without revealing its value:
dotnet user-secrets list | Select-String "JwtSettings:Secret"   # PowerShell
dotnet user-secrets list | grep "JwtSettings:Secret"             # bash

# The app itself fails fast with a clear, non-secret-revealing error message
# at startup if ConnectionStrings:DefaultConnection or JwtSettings:Secret
# (or JwtSettings:Secret being too short) is missing — check the startup logs.
```

Never `echo`, log, or commit the actual secret value.

## 10. Warning

**Never commit real secret values** — not in `appsettings.json`,
`appsettings.Development.json`, `appsettings.Production.json`, `.env` files,
scripts, or commit messages. Use User Secrets locally and environment
variables in production, as described above.
