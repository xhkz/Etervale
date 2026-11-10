# The game server on Linux, built from the repository root (docker-compose.yml, service server; make run-server).
# assets/ and data/ are mounted at run time, never copied: the server finds them beside its executable in /app.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Server/PristonTale.Server/PristonTale.Server.csproj -c Release -o /app -warnaserror

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 10009
ENTRYPOINT ["dotnet", "PristonTale.Server.dll"]
