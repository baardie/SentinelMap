FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/SentinelMap.Api/SentinelMap.Api.csproj", "src/SentinelMap.Api/"]
COPY ["src/SentinelMap.Domain/SentinelMap.Domain.csproj", "src/SentinelMap.Domain/"]
COPY ["src/SentinelMap.Infrastructure/SentinelMap.Infrastructure.csproj", "src/SentinelMap.Infrastructure/"]
COPY ["src/SentinelMap.SharedKernel/SentinelMap.SharedKernel.csproj", "src/SentinelMap.SharedKernel/"]
RUN dotnet restore "src/SentinelMap.Api/SentinelMap.Api.csproj"
COPY . .
WORKDIR "/src/src/SentinelMap.Api"
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "SentinelMap.Api.dll"]
