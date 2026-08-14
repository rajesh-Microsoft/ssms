# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

COPY --from=build /app/publish .

# Chromium is deliberately NOT installed: it added ~2GB to this image and filled the VM disk,
# and no configured provider needs it (TGSPDCL server-renders its bill). To enable the
# Playwright fallback for a provider that does, add:
#   RUN dotnet SMMS.Api.dll install-playwright
# and set Utilities:PlaywrightFallbackEnabled to true.

EXPOSE 8080

ENTRYPOINT ["dotnet","SMMS.Api.dll"]