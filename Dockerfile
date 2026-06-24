# syntax=docker/dockerfile:1

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for restore-layer caching
COPY PropaneDriver.Server/PropaneDriver.Server.csproj PropaneDriver.Server/
COPY PropaneDriver.Client/PropaneDriver.Client.csproj PropaneDriver.Client/
COPY PropaneDriver.Shared/PropaneDriver.Shared.csproj PropaneDriver.Shared/
RUN dotnet restore PropaneDriver.Server/PropaneDriver.Server.csproj

# Copy the rest of the source and publish the host project
# (this also builds the Blazor WASM client and Shared transitively)
COPY . .
RUN dotnet publish PropaneDriver.Server/PropaneDriver.Server.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# .NET 8 containers listen on 8080 by default
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "PropaneDriver.Server.dll"]
