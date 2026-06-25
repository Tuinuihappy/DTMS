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
COPY src/AMR.DeliveryPlanning.Api/AMR.DeliveryPlanning.Api.csproj src/AMR.DeliveryPlanning.Api/
COPY src/AMR.DeliveryPlanning.SharedKernel/AMR.DeliveryPlanning.SharedKernel.csproj src/AMR.DeliveryPlanning.SharedKernel/

# Facility
COPY src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/AMR.DeliveryPlanning.Facility.Domain.csproj src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/
COPY src/Modules/Facility/AMR.DeliveryPlanning.Facility.Application/AMR.DeliveryPlanning.Facility.Application.csproj src/Modules/Facility/AMR.DeliveryPlanning.Facility.Application/
COPY src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure/AMR.DeliveryPlanning.Facility.Infrastructure.csproj src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure/
COPY src/Modules/Facility/AMR.DeliveryPlanning.Facility.Presentation/AMR.DeliveryPlanning.Facility.Presentation.csproj src/Modules/Facility/AMR.DeliveryPlanning.Facility.Presentation/
COPY src/Modules/Facility/AMR.DeliveryPlanning.Facility.IntegrationEvents/AMR.DeliveryPlanning.Facility.IntegrationEvents.csproj src/Modules/Facility/AMR.DeliveryPlanning.Facility.IntegrationEvents/

# Fleet
COPY src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Domain/AMR.DeliveryPlanning.Fleet.Domain.csproj src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Domain/
COPY src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Application/AMR.DeliveryPlanning.Fleet.Application.csproj src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Application/
COPY src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/AMR.DeliveryPlanning.Fleet.Infrastructure.csproj src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/
COPY src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Presentation/AMR.DeliveryPlanning.Fleet.Presentation.csproj src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Presentation/
COPY src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.IntegrationEvents/AMR.DeliveryPlanning.Fleet.IntegrationEvents.csproj src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.IntegrationEvents/

# DeliveryOrder
COPY src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Domain/AMR.DeliveryPlanning.DeliveryOrder.Domain.csproj src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Domain/
COPY src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/AMR.DeliveryPlanning.DeliveryOrder.Application.csproj src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/
COPY src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.csproj src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/
COPY src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Presentation/AMR.DeliveryPlanning.DeliveryOrder.Presentation.csproj src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Presentation/
COPY src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents/AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents.csproj src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents/

# Planning
COPY src/Modules/Planning/AMR.DeliveryPlanning.Planning.Domain/AMR.DeliveryPlanning.Planning.Domain.csproj src/Modules/Planning/AMR.DeliveryPlanning.Planning.Domain/
COPY src/Modules/Planning/AMR.DeliveryPlanning.Planning.Application/AMR.DeliveryPlanning.Planning.Application.csproj src/Modules/Planning/AMR.DeliveryPlanning.Planning.Application/
COPY src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/AMR.DeliveryPlanning.Planning.Infrastructure.csproj src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/
COPY src/Modules/Planning/AMR.DeliveryPlanning.Planning.Presentation/AMR.DeliveryPlanning.Planning.Presentation.csproj src/Modules/Planning/AMR.DeliveryPlanning.Planning.Presentation/
COPY src/Modules/Planning/AMR.DeliveryPlanning.Planning.IntegrationEvents/AMR.DeliveryPlanning.Planning.IntegrationEvents.csproj src/Modules/Planning/AMR.DeliveryPlanning.Planning.IntegrationEvents/

# Dispatch
COPY src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/AMR.DeliveryPlanning.Dispatch.Domain.csproj src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/
COPY src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/AMR.DeliveryPlanning.Dispatch.Application.csproj src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/
COPY src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/AMR.DeliveryPlanning.Dispatch.Infrastructure.csproj src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/
COPY src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Presentation/AMR.DeliveryPlanning.Dispatch.Presentation.csproj src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Presentation/
COPY src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.IntegrationEvents/AMR.DeliveryPlanning.Dispatch.IntegrationEvents.csproj src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.IntegrationEvents/

# OmsAdapter
COPY src/Modules/OmsAdapter/AMR.DeliveryPlanning.OmsAdapter.csproj src/Modules/OmsAdapter/

# Transport.Abstractions (Phase 4.0 rename of VendorAdapter.Abstractions)
COPY src/Modules/Transport.Abstractions/AMR.DeliveryPlanning.Transport.Abstractions/AMR.DeliveryPlanning.Transport.Abstractions.csproj src/Modules/Transport.Abstractions/AMR.DeliveryPlanning.Transport.Abstractions/

# Transport.Amr (Phase 4.0 rename of VendorAdapter.Riot3/Feeder/Infrastructure/Simulator)
COPY src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.csproj src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/
COPY src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Infrastructure/AMR.DeliveryPlanning.Transport.Amr.Infrastructure.csproj src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Infrastructure/
COPY src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Feeder/AMR.DeliveryPlanning.Transport.Amr.Feeder.csproj src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Feeder/
COPY src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Simulator/AMR.DeliveryPlanning.Transport.Amr.Simulator.csproj src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Simulator/

# Transport.Manual (Phase 4.1-4.6)
COPY src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual/AMR.DeliveryPlanning.Transport.Manual.csproj src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual/
COPY src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual.Application/AMR.DeliveryPlanning.Transport.Manual.Application.csproj src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual.Application/
COPY src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual.Infrastructure/AMR.DeliveryPlanning.Transport.Manual.Infrastructure.csproj src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual.Infrastructure/
COPY src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual.Presentation/AMR.DeliveryPlanning.Transport.Manual.Presentation.csproj src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual.Presentation/

RUN dotnet restore src/AMR.DeliveryPlanning.Api/AMR.DeliveryPlanning.Api.csproj

# Copy everything and build
COPY src/ src/
ENV MSBUILDDISABLENODEREUSE=1
RUN dotnet publish src/AMR.DeliveryPlanning.Api/AMR.DeliveryPlanning.Api.csproj -c Release -o /app/publish --no-restore -maxcpucount:1

FROM base AS final
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AMR.DeliveryPlanning.Api.dll"]
