# syntax=docker/dockerfile:1

# ---- build: compile and publish the API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so this slow layer is cached until
# a package or project reference changes (not on every source edit).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY GrocerEase.Api/GrocerEase.Api.csproj                       GrocerEase.Api/
COPY GrocerEase.Application/GrocerEase.Application.csproj       GrocerEase.Application/
COPY GrocerEase.Contracts/GrocerEase.Contracts.csproj           GrocerEase.Contracts/
COPY GrocerEase.Domain/GrocerEase.Domain.csproj                 GrocerEase.Domain/
COPY GrocerEase.Infrastructure/GrocerEase.Infrastructure.csproj GrocerEase.Infrastructure/
RUN dotnet restore GrocerEase.Api/GrocerEase.Api.csproj

COPY . .
RUN dotnet publish GrocerEase.Api/GrocerEase.Api.csproj \
      --configuration Release \
      --no-restore \
      --output /app/publish

# ---- bundle: turn the EF Core migrations into one executable ----
# A "migration bundle" carries the migrations and everything needed to run them, so applying them needs no SDK and no source.
# The dotnet-ef version is pinned in .config/dotnet-tools.json.
FROM build AS bundle
ARG TARGETARCH
RUN dotnet tool restore
# A self-contained executable is built for one runtime, so the packages must be restored for that runtime first.
RUN case "$TARGETARCH" in arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac \
 && dotnet restore GrocerEase.Infrastructure/GrocerEase.Infrastructure.csproj --runtime "$rid" \
 && dotnet ef migrations bundle \
      --project GrocerEase.Infrastructure \
      --configuration Release \
      --self-contained \
      --runtime "$rid" \
      --output /app/efbundle \
      --force

# ---- migrate: runs the bundle once, against the database named by ConnectionStrings__Default, then exits ----
# Self-contained, so only the OS libraries are needed (no .NET runtime). Same non-root user as the API.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrate
WORKDIR /app
COPY --from=bundle /app/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# ---- runtime: the ASP.NET Core runtime only, no SDK ----
# This must stay the LAST stage: it is what "docker build ." builds when no --target is given.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# A container logs to its console (stdout) and the platform keeps, rotates and ships that output: Docker's logging driver, or whatever
# runs the image later. The API runs as a user that can write nowhere in /app, so the image turns the log files off instead of
# creating a folder and a volume for them. To get files anyway, run the image with LogFiles__Enabled=true and a LogFiles__Directory
# on a volume the "app" user can write to.
ENV LogFiles__Enabled=false

# The base image ships a non-root user named "app" and exposes its id as $APP_UID.
# Everything after this line runs as that user. Non-root cannot bind ports below
# 1024, which is why the image listens on 8080 (ASPNETCORE_HTTP_PORTS default).
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "GrocerEase.Api.dll"]
