# =============================================================================
# MediCore Identity Service — Multi-Stage Dockerfile
# =============================================================================
# Stages:
#   build   — SDK image: restore + build
#   test    — runs unit tests; fails the build if tests fail
#   publish — dotnet publish (trimmed, no App Host)
#   final   — ASP.NET runtime, non-root user, health check
#
# Build args (injected by CI via docker/build-push-action):
#   BUILD_DATE — ISO 8601 timestamp of the build
#   GIT_SHA    — full git commit SHA
# =============================================================================

ARG DOTNET_VERSION=8.0

# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy project files first — Docker layer cache: only re-restores when .csproj changes
COPY ["src/services/identity/MediCore.Identity.Api/Api.csproj",              "src/services/identity/MediCore.Identity.Api/"]
COPY ["src/services/identity/MediCore.Identity.Application/Application.csproj", "src/services/identity/MediCore.Identity.Application/"]
COPY ["src/services/identity/MediCore.Identity.Infrastructure/Infrastructure.csproj", "src/services/identity/MediCore.Identity.Infrastructure/"]
COPY ["src/shared/MediCore.Contracts/Contracts.csproj",                       "src/shared/MediCore.Contracts/"]

RUN dotnet restore "src/services/identity/MediCore.Identity.Api/Api.csproj"

# Copy source and build
COPY src/services/identity/ src/services/identity/
COPY src/shared/             src/shared/

WORKDIR "/src/src/services/identity/MediCore.Identity.Api"
RUN dotnet build "Api.csproj" --configuration Release --no-restore

# ── Stage 2: Test ─────────────────────────────────────────────────────────────
FROM build AS test
WORKDIR /src

# Copy test project
COPY ["src/services/identity/MediCore.Identity.Tests/Tests.csproj", "src/services/identity/MediCore.Identity.Tests/"]
RUN dotnet restore "src/services/identity/MediCore.Identity.Tests/Tests.csproj"

COPY src/services/identity/MediCore.Identity.Tests/ src/services/identity/MediCore.Identity.Tests/

# Run tests — this stage fails the build if any test fails
RUN dotnet test "src/services/identity/MediCore.Identity.Tests/Tests.csproj" \
    --configuration Release \
    --no-restore \
    --logger "console;verbosity=normal"

# ── Stage 3: Publish ──────────────────────────────────────────────────────────
FROM build AS publish
WORKDIR "/src/src/services/identity/MediCore.Identity.Api"
RUN dotnet publish "Api.csproj" \
    --configuration Release \
    --no-build \
    --output /app/publish \
    /p:UseAppHost=false

# ── Stage 4: Runtime (final) ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

# Build-time labels for image traceability
ARG BUILD_DATE
ARG GIT_SHA
LABEL org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.title="MediCore Identity Service" \
      org.opencontainers.image.description="Identity and authentication microservice" \
      org.opencontainers.image.vendor="MediCore Team" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app

# Create a non-root system user — never run as root in production
RUN addgroup --system --gid 1000 appgroup \
    && adduser --system --uid 1000 --ingroup appgroup --no-create-home appuser

# Copy published output
COPY --from=publish --chown=appuser:appgroup /app/publish .

USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Health check — used by Docker Compose, Kubernetes liveness probes, and App Service
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]