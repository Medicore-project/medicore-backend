# =============================================================================
# MediCore Patient Service — Multi-Stage Dockerfile
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
COPY ["src/services/patient/MediCore.Patient.Api/Api.csproj",                    "src/services/patient/MediCore.Patient.Api/"]
COPY ["src/services/patient/MediCore.Patient.Application/Application.csproj",    "src/services/patient/MediCore.Patient.Application/"]
COPY ["src/services/patient/MediCore.Patient.Infrastructure/Infrastructure.csproj", "src/services/patient/MediCore.Patient.Infrastructure/"]
COPY ["src/shared/MediCore.Contracts/Contracts.csproj",                           "src/shared/MediCore.Contracts/"]

RUN dotnet restore "src/services/patient/MediCore.Patient.Api/Api.csproj"

# Copy source and build
COPY src/services/patient/ src/services/patient/
COPY src/shared/            src/shared/

WORKDIR "/src/src/services/patient/MediCore.Patient.Api"
RUN dotnet build "Api.csproj" --configuration Release --no-restore

# ── Stage 2: Test ─────────────────────────────────────────────────────────────
FROM build AS test
WORKDIR /src

# Copy test project
COPY ["src/services/patient/MediCore.Patient.Tests/Tests.csproj", "src/services/patient/MediCore.Patient.Tests/"]
RUN dotnet restore "src/services/patient/MediCore.Patient.Tests/Tests.csproj"

COPY src/services/patient/MediCore.Patient.Tests/ src/services/patient/MediCore.Patient.Tests/

# Run tests — this stage fails the build if any test fails
RUN dotnet test "src/services/patient/MediCore.Patient.Tests/Tests.csproj" \
    --configuration Release \
    --no-restore \
    --logger "console;verbosity=normal"

# ── Stage 3: Publish ──────────────────────────────────────────────────────────
FROM build AS publish
WORKDIR "/src/src/services/patient/MediCore.Patient.Api"
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
      org.opencontainers.image.title="MediCore Patient Service" \
      org.opencontainers.image.description="Patient and Medical Records microservice" \
      org.opencontainers.image.vendor="MediCore Team" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app

# Copy published output
COPY --from=publish /app/publish .

USER app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Health check — used by Docker Compose, Kubernetes liveness probes, and App Service
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "MediCore.Patient.Api.dll"]
