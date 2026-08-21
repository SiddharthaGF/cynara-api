FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV HUSKY=0

COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Build.targets", "./"]
COPY ["stylecop.json", "./"]
COPY ["src/Cynara.Api/Cynara.Api.csproj", "src/Cynara.Api/"]
COPY ["src/Cynara.Application/Cynara.Application.csproj", "src/Cynara.Application/"]
COPY ["src/Cynara.Domain/Cynara.Domain.csproj", "src/Cynara.Domain/"]
COPY ["src/Cynara.Infrastructure/Cynara.Infrastructure.csproj", "src/Cynara.Infrastructure/"]
RUN dotnet restore "src/Cynara.Api/Cynara.Api.csproj"

COPY . .
RUN dotnet publish "src/Cynara.Api/Cynara.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN groupadd --system appgroup \
    && useradd --uid 1001 --gid appgroup appuser
USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=10000

EXPOSE 10000

ENTRYPOINT ["dotnet", "Cynara.Api.dll"]
