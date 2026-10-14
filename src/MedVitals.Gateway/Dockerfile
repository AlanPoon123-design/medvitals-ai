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

EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "MedVitals.Gateway.dll"]