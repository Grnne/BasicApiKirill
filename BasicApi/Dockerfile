FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

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

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BasicApi.dll"]