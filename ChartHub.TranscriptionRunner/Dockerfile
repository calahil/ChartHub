# ── stage 1: .NET build ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["ChartHub.TranscriptionRunner/ChartHub.TranscriptionRunner.csproj", "ChartHub.TranscriptionRunner/"]
RUN dotnet restore "ChartHub.TranscriptionRunner/ChartHub.TranscriptionRunner.csproj" -r linux-x64

COPY . .
RUN dotnet publish "ChartHub.TranscriptionRunner/ChartHub.TranscriptionRunner.csproj" \
    -c Release --no-restore \
    -r linux-x64 --self-contained true \
    -o /app/publish

# ── stage 2: Python + basic-pitch runtime ────────────────────────────────────
FROM python:3.11-slim AS final
WORKDIR /app

# The slim base image may not include ICU. Enable invariant mode so .NET
# tools (register/run CLI) can start reliably across hosts.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

RUN apt-get update && apt-get install -y --no-install-recommends \
    libsndfile1 \
    && rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir basic-pitch==0.4.0

RUN groupadd --gid 1000 runner && useradd --uid 1000 --gid 1000 --create-home runner
# /config and /data are the bind-mount points; symlink so the app finds its standard path
RUN mkdir -p /config /data \
    && ln -s /config /home/runner/.charthub-runner \
    && chown -R runner:runner /app /config /data

COPY --from=build /app/publish .
RUN chmod +x ./ChartHub.TranscriptionRunner && chown runner:runner ./ChartHub.TranscriptionRunner

USER runner

VOLUME ["/config", "/data"]

ENTRYPOINT ["./ChartHub.TranscriptionRunner"]
CMD ["run"]
