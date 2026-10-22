# syntax=docker/dockerfile:1
# Raffa Worker host image (ADR-002 thin composition root; ADR-005 Container Apps
# Consumption profile). Built by .github/workflows/backend.yml via:
#   az acr build --file backend/src/Raffa.Worker/Dockerfile backend
# The build context is `backend/`, so every COPY path below is relative to that
# directory, not to this Dockerfile's own location.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Whole-context copy: same rationale as Raffa.Api's Dockerfile -- this is a modular monolith
# (ADR-002) with several project references from Raffa.Worker, so a per-csproj layer-cached
# restore would need a COPY line per project and would still invalidate on every dependency
# change. Simpler and correct first (R0); optimize caching only if build time becomes a real
# problem.
COPY . .

# ReadyToRun, same as Raffa.Api's Dockerfile (see the rationale there): precompiled native code next
# to the IL cuts the JIT work of every cold start of this host, with identical behaviour -- the JIT
# still tiers hot methods up as before. `-r linux-x64` matches the Container Apps hosts,
# `--self-contained false` keeps using the shared runtime of the image below, and
# PublishReadyToRun must be passed to restore too (its packs are restore-time packages; without
# them `publish --no-restore` fails with NETSDK1094).
RUN dotnet restore src/Raffa.Worker/Raffa.Worker.csproj \
    --runtime linux-x64 \
    -p:PublishReadyToRun=true

RUN dotnet publish src/Raffa.Worker/Raffa.Worker.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    -p:PublishReadyToRun=true \
    --no-restore \
    --output /app

# `runtime`, not `aspnet`: Raffa.Worker is a Microsoft.NET.Sdk.Worker host (Generic Host +
# BackgroundService) with no Kestrel/ASP.NET Core dependency, and infra/modules/containerapps'
# "worker" Container App has no ingress block (background processing only) -- the smaller,
# cheaper runtime image is the correct fit, not a leftover copy of the API image.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Run as the non-root user baked into the runtime image (.NET 8+).
USER $APP_UID

ENTRYPOINT ["dotnet", "Raffa.Worker.dll"]
