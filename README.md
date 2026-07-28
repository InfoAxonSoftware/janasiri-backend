# Distribution Management System — Backend

B2B Wholesale Distribution Management System API (Janasiri Distributors) — a .NET 10 Web API
built with Clean Architecture / DDD, JWT auth, SignalR real-time hubs, and PostgreSQL.

## 1. Overview

| Layer | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core 10 |
| Database | PostgreSQL + EF Core (Npgsql) |
| Auth | JWT Bearer (access + refresh tokens, role-based authorization) |
| Real-time | SignalR (notifications, order tracking, support chat) |
| Validation | FluentValidation |
| Logging | Serilog |
| File storage | Configurable public/private storage abstraction — see `FILE_STORAGE_MIGRATION.md` |

## 2. Architecture

```
src/
├── DistributionSystem.Domain          # Entities, enums, value objects
├── DistributionSystem.Infrastructure  # EF Core, repositories, JWT, seed data
├── DistributionSystem.Application     # DTOs, services, validators, file storage
└── DistributionSystem.API             # Controllers, middleware, hubs, Program.cs
tests/
├── DistributionSystem.UnitTests
└── DistributionSystem.IntegrationTests
```

## 3. Local prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A local PostgreSQL instance (or any reachable PostgreSQL server)
- No Docker requirement for day-to-day development

## 4. User Secrets setup

This project never stores real secrets in `appsettings.*.json`. Local development secrets are
configured via .NET User Secrets. See **[CONFIGURATION_SETUP.md](CONFIGURATION_SETUP.md)** for the
full configuration hierarchy and exact commands, e.g.:

```bash
cd src/DistributionSystem.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=DistributionDB;Username=<local-user>;Password=<local-password>"
dotnet user-secrets set "JwtSettings:Secret" "<local-secret-min-32-bytes>"
```

## 5. Restore / build / test

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

## 6. Development run command

```bash
cd src/DistributionSystem.API
dotnet run
```

## 7. Release publish command

```bash
dotnet publish src/DistributionSystem.API/DistributionSystem.API.csproj -c Release -o ./publish --no-self-contained
```

## 8. Secrets warning

**Never commit real secret values** — not in `appsettings.json`, `appsettings.Development.json`,
`appsettings.Production.json`, `.env` files, or commit messages. Local secrets belong in User
Secrets; production secrets belong in environment variables on the hosting platform. See
**[CONFIGURATION_SETUP.md](CONFIGURATION_SETUP.md)** for the required key names.

## 9. Configuration reference

Full configuration hierarchy, required keys, and environment-variable names:
**[CONFIGURATION_SETUP.md](CONFIGURATION_SETUP.md)**

## 10. File storage reference

Runtime file storage design (public/private classification, MonsterASP persistent-path
requirements, migration steps for pre-existing uploaded files) lives one level up in the
deployment workspace: `../FILE_STORAGE_MIGRATION.md`.

## License

Proprietary — Janasiri Distributors
