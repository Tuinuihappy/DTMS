FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

# Install curl so docker-compose healthcheck can probe /health/ready.
# 2-3 MB image size cost; cached layer so rebuilds don't pay it again.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution-level files
COPY Directory.Build.props ./
COPY Directory.Packages.props ./

# Copy all project files for restore
COPY src/DTMS.Api/DTMS.Api.csproj src/DTMS.Api/
COPY src/DTMS.SharedKernel/DTMS.SharedKernel.csproj src/DTMS.SharedKernel/

# Facility
COPY src/Modules/Facility/DTMS.Facility.Domain/DTMS.Facility.Domain.csproj src/Modules/Facility/DTMS.Facility.Domain/
COPY src/Modules/Facility/DTMS.Facility.Application/DTMS.Facility.Application.csproj src/Modules/Facility/DTMS.Facility.Application/
COPY src/Modules/Facility/DTMS.Facility.Infrastructure/DTMS.Facility.Infrastructure.csproj src/Modules/Facility/DTMS.Facility.Infrastructure/
COPY src/Modules/Facility/DTMS.Facility.Presentation/DTMS.Facility.Presentation.csproj src/Modules/Facility/DTMS.Facility.Presentation/
COPY src/Modules/Facility/DTMS.Facility.IntegrationEvents/DTMS.Facility.IntegrationEvents.csproj src/Modules/Facility/DTMS.Facility.IntegrationEvents/

# Fleet
COPY src/Modules/Fleet/DTMS.Fleet.Domain/DTMS.Fleet.Domain.csproj src/Modules/Fleet/DTMS.Fleet.Domain/
COPY src/Modules/Fleet/DTMS.Fleet.Application/DTMS.Fleet.Application.csproj src/Modules/Fleet/DTMS.Fleet.Application/
COPY src/Modules/Fleet/DTMS.Fleet.Infrastructure/DTMS.Fleet.Infrastructure.csproj src/Modules/Fleet/DTMS.Fleet.Infrastructure/
COPY src/Modules/Fleet/DTMS.Fleet.Presentation/DTMS.Fleet.Presentation.csproj src/Modules/Fleet/DTMS.Fleet.Presentation/
COPY src/Modules/Fleet/DTMS.Fleet.IntegrationEvents/DTMS.Fleet.IntegrationEvents.csproj src/Modules/Fleet/DTMS.Fleet.IntegrationEvents/

# DeliveryOrder
COPY src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Domain/DTMS.DeliveryOrder.Domain.csproj src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Domain/
COPY src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/DTMS.DeliveryOrder.Application.csproj src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/
COPY src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Infrastructure/DTMS.DeliveryOrder.Infrastructure.csproj src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Infrastructure/
COPY src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Presentation/DTMS.DeliveryOrder.Presentation.csproj src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Presentation/
COPY src/Modules/DeliveryOrder/DTMS.DeliveryOrder.IntegrationEvents/DTMS.DeliveryOrder.IntegrationEvents.csproj src/Modules/DeliveryOrder/DTMS.DeliveryOrder.IntegrationEvents/

# Planning
COPY src/Modules/Planning/DTMS.Planning.Domain/DTMS.Planning.Domain.csproj src/Modules/Planning/DTMS.Planning.Domain/
COPY src/Modules/Planning/DTMS.Planning.Application/DTMS.Planning.Application.csproj src/Modules/Planning/DTMS.Planning.Application/
COPY src/Modules/Planning/DTMS.Planning.Infrastructure/DTMS.Planning.Infrastructure.csproj src/Modules/Planning/DTMS.Planning.Infrastructure/
COPY src/Modules/Planning/DTMS.Planning.Presentation/DTMS.Planning.Presentation.csproj src/Modules/Planning/DTMS.Planning.Presentation/
COPY src/Modules/Planning/DTMS.Planning.IntegrationEvents/DTMS.Planning.IntegrationEvents.csproj src/Modules/Planning/DTMS.Planning.IntegrationEvents/

# Dispatch
COPY src/Modules/Dispatch/DTMS.Dispatch.Domain/DTMS.Dispatch.Domain.csproj src/Modules/Dispatch/DTMS.Dispatch.Domain/
COPY src/Modules/Dispatch/DTMS.Dispatch.Application/DTMS.Dispatch.Application.csproj src/Modules/Dispatch/DTMS.Dispatch.Application/
COPY src/Modules/Dispatch/DTMS.Dispatch.Infrastructure/DTMS.Dispatch.Infrastructure.csproj src/Modules/Dispatch/DTMS.Dispatch.Infrastructure/
COPY src/Modules/Dispatch/DTMS.Dispatch.Presentation/DTMS.Dispatch.Presentation.csproj src/Modules/Dispatch/DTMS.Dispatch.Presentation/
COPY src/Modules/Dispatch/DTMS.Dispatch.IntegrationEvents/DTMS.Dispatch.IntegrationEvents.csproj src/Modules/Dispatch/DTMS.Dispatch.IntegrationEvents/

# OmsAdapter
COPY src/Modules/OmsAdapter/DTMS.OmsAdapter.csproj src/Modules/OmsAdapter/

# Transport.Abstractions (Phase 4.0 rename of VendorAdapter.Abstractions)
COPY src/Modules/Transport.Abstractions/DTMS.Transport.Abstractions/DTMS.Transport.Abstractions.csproj src/Modules/Transport.Abstractions/DTMS.Transport.Abstractions/

# Transport.Amr (consolidated — Feeder + Simulator removed)
COPY src/Modules/Transport.Amr/DTMS.Transport.Amr/DTMS.Transport.Amr.csproj src/Modules/Transport.Amr/DTMS.Transport.Amr/
COPY src/Modules/Transport.Amr/DTMS.Transport.Amr.Infrastructure/DTMS.Transport.Amr.Infrastructure.csproj src/Modules/Transport.Amr/DTMS.Transport.Amr.Infrastructure/

# Transport.Manual (Phase 4.1-4.6)
COPY src/Modules/Transport.Manual/DTMS.Transport.Manual/DTMS.Transport.Manual.csproj src/Modules/Transport.Manual/DTMS.Transport.Manual/
COPY src/Modules/Transport.Manual/DTMS.Transport.Manual.Application/DTMS.Transport.Manual.Application.csproj src/Modules/Transport.Manual/DTMS.Transport.Manual.Application/
COPY src/Modules/Transport.Manual/DTMS.Transport.Manual.Infrastructure/DTMS.Transport.Manual.Infrastructure.csproj src/Modules/Transport.Manual/DTMS.Transport.Manual.Infrastructure/
COPY src/Modules/Transport.Manual/DTMS.Transport.Manual.Presentation/DTMS.Transport.Manual.Presentation.csproj src/Modules/Transport.Manual/DTMS.Transport.Manual.Presentation/

RUN dotnet restore src/DTMS.Api/DTMS.Api.csproj

# Copy everything and build
COPY src/ src/
ENV MSBUILDDISABLENODEREUSE=1
RUN dotnet publish src/DTMS.Api/DTMS.Api.csproj -c Release -o /app/publish --no-restore -maxcpucount:1

FROM base AS final
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "DTMS.Api.dll"]
