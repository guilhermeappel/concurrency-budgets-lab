FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Lab.Runner/ src/Lab.Runner/
RUN dotnet publish src/Lab.Runner/Lab.Runner.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Lab.Runner.dll"]
