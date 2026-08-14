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

# The direct HTTP provider is primary. Chromium is present for providers whose bill page
# renders dynamically and therefore requires the automatic Playwright fallback.
RUN dotnet SMMS.Api.dll install-playwright

EXPOSE 8080

ENTRYPOINT ["dotnet","SMMS.Api.dll"]