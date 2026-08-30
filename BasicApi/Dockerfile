FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# ── Фронтенд ──
# Отдельная стадия: node нужен только для сборки, в финальный образ он не попадёт.
FROM node:22-alpine AS client
WORKDIR /client

# Сначала манифесты — тогда слой с npm ci переиспользуется, пока зависимости
# не поменялись, и пересборка после правки кода занимает секунды.
COPY BasicWebClient/package.json BasicWebClient/package-lock.json ./
RUN npm ci

COPY BasicWebClient/ ./
# Сборка включает проверку типов (vue-tsc): битые типы валят сборку образа,
# а не всплывают в проде. --outDir переопределяет путь из vite.config,
# который рассчитан на локальную разработку.
RUN npm run build -- --outDir dist

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# ✅ Копируем оба csproj до restore
COPY ["BasicApi/BasicApi.csproj", "BasicApi/"]
COPY ["BasicApi.Storage/BasicApi.Storage.csproj", "BasicApi.Storage/"]
RUN dotnet restore "./BasicApi/BasicApi.csproj"

COPY . .
WORKDIR "/src/BasicApi"
RUN dotnet build "./BasicApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BasicApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Собранный фронтенд кладём в статику API — раздаёт его Kestrel.
COPY --from=client /client/dist /app/publish/wwwroot/client

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BasicApi.dll"]