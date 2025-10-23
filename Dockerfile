# syntax=docker/dockerfile:1.7-labs

# -------------------- Build stage --------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first to leverage Docker layer caching
COPY ./IPSDatastreamWorker.sln ./
COPY ./src/IPSDatastreamWorker.Domain/IPSDatastreamWorker.Domain.csproj ./src/IPSDatastreamWorker.Domain/
COPY ./src/IPSDatastreamWorker.Application/IPSDatastreamWorker.Application.csproj ./src/IPSDatastreamWorker.Application/
COPY ./src/IPSDatastreamWorker.Infrastructure/IPSDatastreamWorker.Infrastructure.csproj ./src/IPSDatastreamWorker.Infrastructure/
COPY ./src/IPSDatastreamWorker.Worker/IPSDatastreamWorker.Worker.csproj ./src/IPSDatastreamWorker.Worker/

# Restore
RUN dotnet restore ./IPSDatastreamWorker.sln

# Copy the rest of the source
COPY ./src ./src

# Publish (Release)
RUN dotnet publish ./src/IPSDatastreamWorker.Worker/IPSDatastreamWorker.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

# -------------------- Runtime stage --------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Set environment defaults
ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IPSDatastreamWorker.Worker.dll"]

