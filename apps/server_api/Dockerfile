# Build from repository root:
# docker build -f apps/server_api/Dockerfile -t zarr-api:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY apps/server_api/ERPAccounting.Api.csproj apps/server_api/
RUN dotnet restore apps/server_api/ERPAccounting.Api.csproj

COPY apps/server_api/ apps/server_api/
WORKDIR /src/apps/server_api
RUN dotnet publish ERPAccounting.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    Database__Provider=Sqlite \
    ConnectionStrings__Sqlite="Data Source=/app/data/erp_accounting.db"

RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app
COPY --from=build /app/publish/ ./

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "ERPAccounting.Api.dll"]
