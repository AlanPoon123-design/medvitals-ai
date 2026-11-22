# C# app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MedVitals.Gateway.csproj", "./"]
RUN dotnet restore "MedVitals.Gateway.csproj"
COPY . .
RUN dotnet publish "MedVitals.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# runtime container
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# run as non-root user
USER $APP_UID

# .NET 8 aspnet images listen on 8080 by default (ASPNETCORE_HTTP_PORTS=8080);
# TLS is expected to be terminated by a reverse proxy in front of the gateway
EXPOSE 8080
ENTRYPOINT ["dotnet", "MedVitals.Gateway.dll"]