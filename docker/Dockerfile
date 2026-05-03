FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SchoolManagement.API/SchoolManagement.API.csproj", "SchoolManagement.API/"]
COPY ["SchoolManagement.Application/SchoolManagement.Application.csproj", "SchoolManagement.Application/"]
COPY ["SchoolManagement.Domain/SchoolManagement.Domain.csproj", "SchoolManagement.Domain/"]
COPY ["SchoolManagement.Infrastructure/SchoolManagement.Infrastructure.csproj", "SchoolManagement.Infrastructure/"]
RUN dotnet restore "SchoolManagement.API/SchoolManagement.API.csproj"
COPY . .
WORKDIR "/src/SchoolManagement.API"
RUN dotnet build "SchoolManagement.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SchoolManagement.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SchoolManagement.API.dll"]
