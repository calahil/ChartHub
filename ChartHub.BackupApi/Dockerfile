FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["ChartHub.BackupApi/ChartHub.BackupApi.csproj", "ChartHub.BackupApi/"]
RUN dotnet restore "ChartHub.BackupApi/ChartHub.BackupApi.csproj"

COPY . .
RUN dotnet publish "ChartHub.BackupApi/ChartHub.BackupApi.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

RUN mkdir -p /cache/downloads /cache/images

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ChartHub.BackupApi.dll"]
