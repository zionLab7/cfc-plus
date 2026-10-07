# Build context is an explicit source allowlist. Never COPY the working directory wholesale.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY CfcPilot.csproj ./
RUN dotnet restore
COPY Program.cs appsettings.json ./
COPY Domain ./Domain
COPY Application ./Application
COPY Infrastructure ./Infrastructure
COPY Config ./Config
COPY wwwroot ./wwwroot
COPY native-host ./native-host
RUN dotnet publish -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl tini tzdata && rm -rf /var/lib/apt/lists/* && mkdir -p /data /app && chown -R 1654:1654 /data /app
WORKDIR /app
COPY --from=build --chown=1654:1654 /out ./
ENV ASPNETCORE_ENVIRONMENT=Production CFC_DATA_DIR=/data CFC_INSTALLATION_MODE=commercial NativePortal__Enabled=false TZ=America/Sao_Paulo
USER 1654:1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 CMD curl --fail --silent http://127.0.0.1:8080/api/health || exit 1
ENTRYPOINT ["/usr/bin/tini","--"]
CMD ["dotnet","CfcPilot.dll","--urls","http://0.0.0.0:8080"]

# Interactive browser is an explicit image variant. Chromium keeps its sandbox.
FROM node:22-bookworm-slim AS electron
WORKDIR /browser
RUN npm init -y && npm install --save-exact electron@44.5.1 && node node_modules/electron/install.js

FROM runtime AS browser
USER root
RUN apt-get update && apt-get install -y --no-install-recommends xvfb xauth libnss3 libatk-bridge2.0-0t64 libgtk-3-0t64 libgbm1 libasound2t64 libxss1 libdrm2 libxshmfence1 fonts-liberation dbus-x11 && rm -rf /var/lib/apt/lists/*
COPY --from=electron /browser/node_modules/electron/dist /opt/electron
RUN chown root:root /opt/electron/chrome-sandbox && chmod 4755 /opt/electron/chrome-sandbox
COPY --chown=1654:1654 infra/production/start-browser.sh /app/start-browser.sh
RUN chmod 755 /app/start-browser.sh
ENV NativePortal__UseUserNamespaceSandbox=true NativePortal__Enabled=true NativePortal__ExecutablePath=/opt/electron/electron PortalBrowser__Engine=chromium PortalBrowser__ExecutablePath=/opt/electron/electron HOME=/data/home
USER 1654:1654
CMD ["/app/start-browser.sh"]

FROM browser AS browser-check
USER root
COPY --from=electron /usr/local/bin/node /usr/local/bin/node
COPY --chown=1654:1654 tools/NativePortalChecks.cjs /app/tools/NativePortalChecks.cjs
USER 1654:1654
