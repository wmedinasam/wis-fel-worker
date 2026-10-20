FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/WisFel.Worker/WisFel.Worker.csproj", "src/WisFel.Worker/"]
RUN dotnet restore "src/WisFel.Worker/WisFel.Worker.csproj"

COPY . .
WORKDIR /src/src/WisFel.Worker
RUN dotnet publish "WisFel.Worker.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Agent token store and its Data Protection key ring. An empty named volume mounted here inherits these
# permissions, so the non-root user can write it.
RUN mkdir -p /var/lib/wis-fel-worker \
    && chown $APP_UID /var/lib/wis-fel-worker \
    && chmod 700 /var/lib/wis-fel-worker
ENV FelApi__TokenStorePath=/var/lib/wis-fel-worker

USER $APP_UID

ENTRYPOINT ["dotnet", "WisFel.Worker.dll"]
