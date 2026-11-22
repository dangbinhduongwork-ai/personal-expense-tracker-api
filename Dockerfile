FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore using layer caching before copying application source files
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "ExpenseTracker.slnx", "./"]
COPY ["src/ExpenseTracker.Application/ExpenseTracker.Application.csproj", "src/ExpenseTracker.Application/"]
COPY ["src/ExpenseTracker.Infrastructure/ExpenseTracker.Infrastructure.csproj", "src/ExpenseTracker.Infrastructure/"]
COPY ["src/ExpenseTracker.Api/ExpenseTracker.Api.csproj", "src/ExpenseTracker.Api/"]
RUN dotnet restore src/ExpenseTracker.Api/ExpenseTracker.Api.csproj

COPY src/ src/
RUN dotnet publish src/ExpenseTracker.Api/ExpenseTracker.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ExpenseTracker.Api.dll"]
