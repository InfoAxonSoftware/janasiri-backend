# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for better caching
COPY DistributionSystem.slnx .
COPY src/DistributionSystem.Domain/DistributionSystem.Domain.csproj src/DistributionSystem.Domain/
COPY src/DistributionSystem.Infrastructure/DistributionSystem.Infrastructure.csproj src/DistributionSystem.Infrastructure/
COPY src/DistributionSystem.Application/DistributionSystem.Application.csproj src/DistributionSystem.Application/
COPY src/DistributionSystem.API/DistributionSystem.API.csproj src/DistributionSystem.API/
COPY tests/DistributionSystem.UnitTests/DistributionSystem.UnitTests.csproj tests/DistributionSystem.UnitTests/
COPY tests/DistributionSystem.IntegrationTests/DistributionSystem.IntegrationTests.csproj tests/DistributionSystem.IntegrationTests/

# Restore packages
RUN dotnet restore

# Copy everything and build
COPY . .
RUN dotnet publish src/DistributionSystem.API/DistributionSystem.API.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Copy published output
COPY --from=build /app/publish .

# Create logs directory
RUN mkdir -p /app/logs && chown -R appuser:appgroup /app/logs

# Switch to non-root user
USER appuser

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "DistributionSystem.API.dll"]
