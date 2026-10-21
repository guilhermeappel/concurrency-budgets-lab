FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Lab.Stubs/ src/Lab.Stubs/
RUN dotnet publish src/Lab.Stubs/Lab.Stubs.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV STUBS_URL=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "Lab.Stubs.dll"]
