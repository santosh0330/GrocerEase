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

# ---- runtime: the ASP.NET Core runtime only, no SDK ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# The base image ships a non-root user named "app" and exposes its id as $APP_UID.
# Everything after this line runs as that user. Non-root cannot bind ports below
# 1024, which is why the image listens on 8080 (ASPNETCORE_HTTP_PORTS default).
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "GrocerEase.Api.dll"]
