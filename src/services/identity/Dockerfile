# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for better Docker layer caching
COPY ["src/services/identity/MediCore.Identity.Api/Api.csproj", "src/services/identity/MediCore.Identity.Api/"]
COPY ["src/services/identity/MediCore.Identity.Application/Application.csproj", "src/services/identity/MediCore.Identity.Application/"]
COPY ["src/services/identity/MediCore.Identity.Infrastructure/Infrastructure.csproj", "src/services/identity/MediCore.Identity.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "src/services/identity/MediCore.Identity.Api/Api.csproj"

# Copy source code
COPY src/services/identity/ src/services/identity/

# Build
WORKDIR "/src/src/services/identity/MediCore.Identity.Api"
RUN dotnet build "Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=publish /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Api.dll"]
