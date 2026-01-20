# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY GitAgent.sln .
COPY src/app/GitAgent.Server/GitAgent.Server.csproj src/app/GitAgent.Server/
COPY src/shared/GitAgent.Shared/GitAgent.Shared.csproj src/shared/GitAgent.Shared/

# Restore dependencies
RUN dotnet restore src/app/GitAgent.Server/GitAgent.Server.csproj

# Copy the rest of the source code
COPY src/ src/

# Build the application
WORKDIR /src/src/app/GitAgent.Server
RUN dotnet build -c Release -o /app/build

# Publish the application
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "GitAgent.Server.dll"]
