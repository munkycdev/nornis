FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG IMAGE_SOURCE=""
ARG IMAGE_REVISION=""
WORKDIR /src

# Copy solution and project files for restore
COPY ["Nornis.sln", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Nornis.Domain/Nornis.Domain.csproj", "src/Nornis.Domain/"]
COPY ["src/Nornis.Shared/Nornis.Shared.csproj", "src/Nornis.Shared/"]
COPY ["src/Nornis.Application/Nornis.Application.csproj", "src/Nornis.Application/"]
COPY ["src/Nornis.Infrastructure/Nornis.Infrastructure.csproj", "src/Nornis.Infrastructure/"]
COPY ["src/Nornis.Api/Nornis.Api.csproj", "src/Nornis.Api/"]
COPY ["src/Nornis.Web/Nornis.Web.csproj", "src/Nornis.Web/"]
COPY ["src/Nornis.Worker/Nornis.Worker.csproj", "src/Nornis.Worker/"]
RUN dotnet restore "src/Nornis.Web/Nornis.Web.csproj"

# Copy full source and publish
COPY . .
RUN dotnet publish "src/Nornis.Web/Nornis.Web.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
ARG IMAGE_SOURCE=""
ARG IMAGE_REVISION=""
LABEL org.opencontainers.image.source="${IMAGE_SOURCE}"
LABEL org.opencontainers.image.revision="${IMAGE_REVISION}"
WORKDIR /app
EXPOSE 8080
RUN adduser --disabled-password --gecos "" appuser
USER appuser
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Nornis.Web.dll"]
