# One build stage feeds all three service images: the solution restores and compiles
# once, and each final stage copies only its own publish output. Build with
# `--target api|web|worker`. Replaces the three per-service Dockerfiles that each
# compiled the whole solution from scratch.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first so restore caches independently of source changes
COPY ["Nornis.sln", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Nornis.Domain/Nornis.Domain.csproj", "src/Nornis.Domain/"]
COPY ["src/Nornis.Shared/Nornis.Shared.csproj", "src/Nornis.Shared/"]
COPY ["src/Nornis.Application/Nornis.Application.csproj", "src/Nornis.Application/"]
COPY ["src/Nornis.Infrastructure/Nornis.Infrastructure.csproj", "src/Nornis.Infrastructure/"]
COPY ["src/Nornis.Api/Nornis.Api.csproj", "src/Nornis.Api/"]
COPY ["src/Nornis.Web/Nornis.Web.csproj", "src/Nornis.Web/"]
COPY ["src/Nornis.Worker/Nornis.Worker.csproj", "src/Nornis.Worker/"]
RUN dotnet restore "src/Nornis.Api/Nornis.Api.csproj" \
 && dotnet restore "src/Nornis.Web/Nornis.Web.csproj" \
 && dotnet restore "src/Nornis.Worker/Nornis.Worker.csproj"

COPY . .
# No --no-restore here: under the .NET 10 SDK, restore computes the static-web-assets
# projection, and a restore done against the csproj-only layer (no wwwroot, no sources)
# leaves the framework scripts (_framework/blazor.web.js) out of the publish output
# entirely. The earlier restore layer still pre-warms the NuGet cache, so this re-restore
# is cheap.
RUN dotnet publish "src/Nornis.Api/Nornis.Api.csproj" -c Release -o /app/api \
 && dotnet publish "src/Nornis.Web/Nornis.Web.csproj" -c Release -o /app/web \
 && dotnet publish "src/Nornis.Worker/Nornis.Worker.csproj" -c Release -o /app/worker

# ----------------------------------------------------------------------- api --
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
ARG IMAGE_SOURCE=""
ARG IMAGE_REVISION=""
LABEL org.opencontainers.image.source="${IMAGE_SOURCE}"
LABEL org.opencontainers.image.revision="${IMAGE_REVISION}"
WORKDIR /app
EXPOSE 8080
USER app
COPY --from=build /app/api .
ENTRYPOINT ["dotnet", "Nornis.Api.dll"]

# ----------------------------------------------------------------------- web --
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
ARG IMAGE_SOURCE=""
ARG IMAGE_REVISION=""
LABEL org.opencontainers.image.source="${IMAGE_SOURCE}"
LABEL org.opencontainers.image.revision="${IMAGE_REVISION}"
WORKDIR /app
EXPOSE 8080
USER app
COPY --from=build /app/web .
ENTRYPOINT ["dotnet", "Nornis.Web.dll"]

# -------------------------------------------------------------------- worker --
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker
ARG IMAGE_SOURCE=""
ARG IMAGE_REVISION=""
LABEL org.opencontainers.image.source="${IMAGE_SOURCE}"
LABEL org.opencontainers.image.revision="${IMAGE_REVISION}"
WORKDIR /app
USER app
COPY --from=build /app/worker .
ENTRYPOINT ["dotnet", "Nornis.Worker.dll"]
