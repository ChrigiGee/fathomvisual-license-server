# Dockerfile for FathomVisual.LicenseServer
# Multi-stage build for .NET 9.0

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["FathomVisual.LicenseServer.csproj", "./"]
RUN dotnet restore "FathomVisual.LicenseServer.csproj"

# Copy source code and build
COPY . .
RUN dotnet build "FathomVisual.LicenseServer.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "FathomVisual.LicenseServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos "" appuser

# Copy published application
COPY --from=publish /app/publish .

# Create directory for keys and set permissions
RUN mkdir -p /app/keys && chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose Render.com default port
EXPOSE 10000

# Set environment variables
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:10000/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "FathomVisual.LicenseServer.dll"]
