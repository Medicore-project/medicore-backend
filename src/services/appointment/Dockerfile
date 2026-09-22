# =============================================================================
# MediCore Appointment Service — Multi-Stage Dockerfile
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
COPY ["src/services/appointment/MediCore.Appointment.Api/Api.csproj",                       "src/services/appointment/MediCore.Appointment.Api/"]
COPY ["src/services/appointment/MediCore.Appointment.Application/Application.csproj",       "src/services/appointment/MediCore.Appointment.Application/"]
COPY ["src/services/appointment/MediCore.Appointment.Infrastructure/Infrastructure.csproj", "src/services/appointment/MediCore.Appointment.Infrastructure/"]
COPY ["src/shared/MediCore.Contracts/Contracts.csproj",                                      "src/shared/MediCore.Contracts/"]

RUN dotnet restore "src/services/appointment/MediCore.Appointment.Api/Api.csproj"

# Copy source and build
COPY src/services/appointment/ src/services/appointment/
COPY src/shared/               src/shared/

WORKDIR "/src/src/services/appointment/MediCore.Appointment.Api"
RUN dotnet build "Api.csproj" --configuration Release --no-restore

# ── Stage 2: Test ─────────────────────────────────────────────────────────────
# The test project is currently empty — QA owns the tests for this service — so
# this stage passes trivially. It exists so that adding a test file starts
# gating the image immediately, with no Dockerfile change needed.
FROM build AS test
WORKDIR /src

COPY ["src/services/appointment/MediCore.Appointment.Tests/Tests.csproj", "src/services/appointment/MediCore.Appointment.Tests/"]
RUN dotnet restore "src/services/appointment/MediCore.Appointment.Tests/Tests.csproj"

COPY src/services/appointment/MediCore.Appointment.Tests/ src/services/appointment/MediCore.Appointment.Tests/

RUN dotnet test "src/services/appointment/MediCore.Appointment.Tests/Tests.csproj" \
    --configuration Release \
    --no-restore \
    --logger "console;verbosity=normal"

# ── Stage 3: Publish ──────────────────────────────────────────────────────────
FROM build AS publish
WORKDIR "/src/src/services/appointment/MediCore.Appointment.Api"
RUN dotnet publish "Api.csproj" \
    --configuration Release \
    --no-build \
    --output /app/publish \
    /p:UseAppHost=false

# ── Stage 4: Runtime (final) ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

ARG BUILD_DATE
ARG GIT_SHA
LABEL org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.title="MediCore Appointment Service" \
      org.opencontainers.image.description="Doctor scheduling and appointment slot microservice" \
      org.opencontainers.image.vendor="MediCore Team" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app

COPY --from=publish /app/publish .

# wget backs the HEALTHCHECK below and the compose healthcheck. Unlike the patient
# image this one needs no font packages — the appointment service renders no PDFs.
USER root
RUN apt-get update && apt-get install -y wget && rm -rf /var/lib/apt/lists/*
USER app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "MediCore.Appointment.Api.dll"]
