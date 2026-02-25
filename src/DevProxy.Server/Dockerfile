FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY DevProxy.Shared/DevProxy.Shared.csproj DevProxy.Shared/
COPY DevProxy.Server/DevProxy.Server.csproj DevProxy.Server/
RUN dotnet restore DevProxy.Server/DevProxy.Server.csproj

COPY DevProxy.Shared/ DevProxy.Shared/
COPY DevProxy.Server/ DevProxy.Server/
RUN dotnet publish DevProxy.Server/DevProxy.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "DevProxy.Server.dll"]
