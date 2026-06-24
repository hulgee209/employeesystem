# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EmployeeSystem.csproj ./
RUN dotnet restore ./EmployeeSystem.csproj

COPY . ./
RUN dotnet publish ./EmployeeSystem.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "EmployeeSystem.dll"]
