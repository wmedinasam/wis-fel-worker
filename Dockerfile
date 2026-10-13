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
USER $APP_UID

ENTRYPOINT ["dotnet", "WisFel.Worker.dll"]
