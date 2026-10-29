FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY src/Ocr.Api/Ocr.Api.csproj src/Ocr.Api/
RUN dotnet restore src/Ocr.Api/Ocr.Api.csproj -p:RuntimeIdentifier=linux-x64 -p:PublishAot=true
COPY src/Ocr.Api/ src/Ocr.Api/
RUN dotnet publish src/Ocr.Api/Ocr.Api.csproj -c Release -r linux-x64 --no-restore -o /app \
    -p:PublishAot=true -p:InvariantGlobalization=true -p:StripSymbols=true \
    -p:OptimizationPreference=Size -p:IlcOptimizationPreference=Size \
    -p:DebuggerSupport=false -p:EventSourceSupport=false

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/Ocr.Api ./Ocr.Api
COPY --from=build /app/appsettings.json ./appsettings.json
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["/app/Ocr.Api"]
