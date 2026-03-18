FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/SentinelMap.Workers/SentinelMap.Workers.csproj", "src/SentinelMap.Workers/"]
COPY ["src/SentinelMap.Domain/SentinelMap.Domain.csproj", "src/SentinelMap.Domain/"]
COPY ["src/SentinelMap.Infrastructure/SentinelMap.Infrastructure.csproj", "src/SentinelMap.Infrastructure/"]
COPY ["src/SentinelMap.SharedKernel/SentinelMap.SharedKernel.csproj", "src/SentinelMap.SharedKernel/"]
RUN dotnet restore "src/SentinelMap.Workers/SentinelMap.Workers.csproj"
COPY . .
WORKDIR "/src/src/SentinelMap.Workers"
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SentinelMap.Workers.dll"]
