# Mangrove backend image (spec §12). Multi-arch friendly.
# NOTE: Built/validated in a later phase. SMB libraries need NO host mounts; only mount a host
# path if you configure a Local library. Persist mangrove.db and the cover cache via volumes.

# ---- Build the web UI ----
FROM node:20-alpine AS web
WORKDIR /web
COPY apps/web/package*.json ./
RUN npm ci
COPY apps/web/ ./
RUN npm run build

# ---- Build the server ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS server-build
WORKDIR /src
COPY apps/server/ ./apps/server/
RUN dotnet publish apps/server/Mangrove.Server.csproj -c Release -o /app/publish

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
# SkiaSharp needs native deps on Linux; add SkiaSharp.NativeAssets.Linux to the csproj and the
# fontconfig/libfontconfig packages here when enabling Docker builds.
COPY --from=server-build /app/publish ./
COPY --from=web /web/dist ./wwwroot
ENV MANGROVE_PORT=5000 \
    MANGROVE_DB_PATH=/data/mangrove.db \
    MANGROVE_CACHE_DIR=/data/cache
VOLUME ["/data"]
EXPOSE 5000
ENTRYPOINT ["dotnet", "Mangrove.Server.dll"]
