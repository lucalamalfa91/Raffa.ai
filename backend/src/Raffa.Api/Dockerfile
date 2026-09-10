# syntax=docker/dockerfile:1
# Raffa API host image (ADR-002 thin composition root; ADR-005 Container Apps
# Consumption profile). Built by .github/workflows/backend.yml via:
#   az acr build --file backend/src/Raffa.Api/Dockerfile backend
# The build context is `backend/`, so every COPY path below is relative to that
# directory, not to this Dockerfile's own location.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Whole-context copy: this is a modular monolith (ADR-002) with 10+ project
# references from Raffa.Api, so a per-csproj layer-cached restore would need
# a COPY line per project and would still invalidate on every dependency
# change. Simpler and correct first (R0); optimize caching only if build time
# becomes a real problem.
COPY . .

RUN dotnet restore src/Raffa.Api/Raffa.Api.csproj

RUN dotnet publish src/Raffa.Api/Raffa.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# infra/modules/containerapps sets ingress target_port = 8080 (ADR-005); Kestrel
# must listen there inside the container.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Run as the non-root user baked into the aspnet runtime image (.NET 8+).
USER $APP_UID

ENTRYPOINT ["dotnet", "Raffa.Api.dll"]
