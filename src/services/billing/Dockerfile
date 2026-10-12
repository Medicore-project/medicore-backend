ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY ["src/services/billing/MediCore.Billing.Api/Api.csproj", "src/services/billing/MediCore.Billing.Api/"]
COPY ["src/services/billing/MediCore.Billing.Application/Application.csproj", "src/services/billing/MediCore.Billing.Application/"]
COPY ["src/services/billing/MediCore.Billing.Infrastructure/Infrastructure.csproj", "src/services/billing/MediCore.Billing.Infrastructure/"]
COPY ["src/shared/MediCore.Contracts/Contracts.csproj", "src/shared/MediCore.Contracts/"]
COPY infra/docker/restore-with-retry.sh /usr/local/bin/restore-with-retry.sh
RUN --mount=type=cache,id=medicore-nuget,target=/root/.nuget/packages,sharing=locked \
    sh /usr/local/bin/restore-with-retry.sh "src/services/billing/MediCore.Billing.Api/Api.csproj"
COPY src/services/billing/ src/services/billing/
COPY src/shared/ src/shared/
WORKDIR "/src/src/services/billing/MediCore.Billing.Api"
RUN --mount=type=cache,id=medicore-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet build "Api.csproj" --configuration Release --no-restore

FROM build AS test
WORKDIR /src
COPY ["src/services/billing/MediCore.Billing.Tests/Tests.csproj", "src/services/billing/MediCore.Billing.Tests/"]
RUN --mount=type=cache,id=medicore-nuget,target=/root/.nuget/packages,sharing=locked \
    sh /usr/local/bin/restore-with-retry.sh "src/services/billing/MediCore.Billing.Tests/Tests.csproj"
COPY src/services/billing/MediCore.Billing.Tests/ src/services/billing/MediCore.Billing.Tests/
RUN --mount=type=cache,id=medicore-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet test "src/services/billing/MediCore.Billing.Tests/Tests.csproj" --configuration Release --no-restore

FROM build AS publish
WORKDIR "/src/src/services/billing/MediCore.Billing.Api"
RUN --mount=type=cache,id=medicore-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "Api.csproj" --configuration Release --no-build --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER root
RUN sed -i "s|http://deb.debian.org|https://deb.debian.org|g" /etc/apt/sources.list.d/debian.sources \
    && apt-get update && apt-get install -y wget && rm -rf /var/lib/apt/lists/*
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production DOTNET_RUNNING_IN_CONTAINER=true
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "MediCore.Billing.Api.dll"]
