FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# ── Frontend ──
# A separate stage: node is needed only for the build and does not reach the final image.
FROM node:22-alpine AS client
WORKDIR /client

# Manifests first: the npm ci layer is reused while dependencies stay the same,
# so a rebuild after a code change takes seconds.
COPY BasicWebClient/package.json BasicWebClient/package-lock.json ./
RUN npm ci

COPY BasicWebClient/ ./
# The build includes type checking (vue-tsc): broken types fail the image build
# instead of surfacing in production. --outDir overrides the path from vite.config,
# which is meant for local development.
RUN npm run build -- --outDir dist

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# ✅ Copy both csproj files before restore
COPY ["BasicApi/BasicApi.csproj", "BasicApi/"]
COPY ["BasicApi.Storage/BasicApi.Storage.csproj", "BasicApi.Storage/"]
RUN dotnet restore "./BasicApi/BasicApi.csproj"

COPY . .
WORKDIR "/src/BasicApi"
RUN dotnet build "./BasicApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BasicApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# The built frontend goes into the API's static files; Kestrel serves it.
COPY --from=client /client/dist /app/publish/wwwroot/client

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BasicApi.dll"]