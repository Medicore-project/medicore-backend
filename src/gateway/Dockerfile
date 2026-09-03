FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first (layer-cache friendly)
COPY ["src/gateway/MediCore.Gateway/Gateway.csproj", "src/gateway/MediCore.Gateway/"]
RUN dotnet restore "src/gateway/MediCore.Gateway/Gateway.csproj"

# Copy source and build
COPY src/gateway/MediCore.Gateway/ src/gateway/MediCore.Gateway/
WORKDIR "/src/src/gateway/MediCore.Gateway"
RUN dotnet build "Gateway.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage — minimal image, non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

USER app

COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "MediCore.Gateway.dll"]
