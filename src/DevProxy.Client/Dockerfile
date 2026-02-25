FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY DevProxy.Shared/DevProxy.Shared.csproj DevProxy.Shared/
COPY DevProxy.Client/DevProxy.Client.csproj DevProxy.Client/
RUN dotnet restore DevProxy.Client/DevProxy.Client.csproj

COPY DevProxy.Shared/ DevProxy.Shared/
COPY DevProxy.Client/ DevProxy.Client/
RUN dotnet publish DevProxy.Client/DevProxy.Client.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "DevProxy.Client.dll", "connect"]
