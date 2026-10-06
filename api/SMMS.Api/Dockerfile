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
# and no configured provider needs it (TGSPDCL exposes a JSON bill API). To enable browser
# automation for a future provider, add:
#   RUN dotnet SMMS.Api.dll install-playwright

EXPOSE 8080

ENTRYPOINT ["dotnet","SMMS.Api.dll"]