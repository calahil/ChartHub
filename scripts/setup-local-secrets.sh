#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHART_HUB_PROJECT="${ROOT_DIR}/ChartHub/ChartHub.csproj"
SERVER_PROJECT="${ROOT_DIR}/ChartHub.Server/ChartHub.Server.csproj"
BACKUP_API_PROJECT="${ROOT_DIR}/ChartHub.BackupApi/ChartHub.BackupApi.csproj"

SOURCE_ENV_FILE=""
OUTPUT_ENV_FILE="${ROOT_DIR}/.env.local"
STRICT_MODE=true
APPLY_USER_SECRETS=false
DRY_RUN=false

declare -A FILE_VALUES
declare -A EFFECTIVE_VALUES

print_usage() {
  cat <<'EOF'
Usage: scripts/setup-local-secrets.sh [options]

Generates a ChartHub .env.local file and optionally applies dotnet user-secrets.

Options:
  --env-file <path>       Seed values from this env file.
  --output <path>         Output path for generated env file (default: .env.local).
  --strict                Fail when required values are placeholders (default).
  --no-strict             Allow placeholder values.
  --apply-user-secrets    Apply mapped values to user-secrets for ChartHub, Server, BackupApi.
  --dry-run               Preview changes without writing files or user-secrets.
  -h, --help              Show this help.

Notes:
  - This script never writes .env.
  - If no --env-file is provided, it uses .env.local when present, otherwise .env.example.
EOF
}

log() {
  printf '[setup-local-secrets] %s\n' "$1"
}

fail() {
  printf '[setup-local-secrets] ERROR: %s\n' "$1" >&2
  exit 1
}

is_placeholder() {
  local value="$1"
  local upper
  upper="$(printf '%s' "$value" | tr '[:lower:]' '[:upper:]')"
  [[ -z "$value" || "$upper" == "CHANGE_ME" || "$upper" == CHANGE_ME_* || "$value" == "<REQUIRED>" ]]
}

read_env_file() {
  local env_file="$1"
  [[ -f "$env_file" ]] || return 0

  local line
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "${line// }" ]] && continue
    [[ "${line#\#}" != "$line" ]] && continue
    [[ "$line" != *=* ]] && continue

    local key="${line%%=*}"
    local value="${line#*=}"

    key="${key%%[[:space:]]*}"
    [[ -z "$key" ]] && continue

    # Drop a single pair of surrounding quotes for portability.
    if [[ "$value" =~ ^\".*\"$ ]]; then
      value="${value:1:${#value}-2}"
    elif [[ "$value" =~ ^\'.*\'$ ]]; then
      value="${value:1:${#value}-2}"
    fi

    FILE_VALUES["$key"]="$value"
  done < "$env_file"
}

generate_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 48 | tr -d '\n'
  else
    head -c 48 /dev/urandom | base64 | tr -d '\n'
  fi
}

env_value_or_default() {
  local key="$1"
  local default_value="$2"

  if [[ -n "${!key:-}" ]]; then
    printf '%s' "${!key}"
    return
  fi

  if [[ -n "${FILE_VALUES[$key]+x}" ]]; then
    printf '%s' "${FILE_VALUES[$key]}"
    return
  fi

  printf '%s' "$default_value"
}

set_effective() {
  local key="$1"
  local default_value="$2"
  EFFECTIVE_VALUES["$key"]="$(env_value_or_default "$key" "$default_value")"
}

ensure_user_secrets_id() {
  local project_path="$1"
  if ! grep -q '<UserSecretsId>' "$project_path"; then
    dotnet user-secrets init --project "$project_path" >/dev/null
  fi
}

set_user_secret() {
  local project_path="$1"
  local key="$2"
  local value="$3"

  if [[ -z "$value" ]]; then
    return
  fi

  if [[ "$DRY_RUN" == true ]]; then
    log "[dry-run] user-secrets set ${key} (${project_path##*/})"
    return
  fi

  dotnet user-secrets set "$key" "$value" --project "$project_path" >/dev/null
}

redact() {
  local value="$1"
  if [[ -z "$value" ]]; then
    printf '%s' '<empty>'
    return
  fi

  if (( ${#value} <= 8 )); then
    printf '%s' '<redacted>'
    return
  fi

  printf '%s...%s' "${value:0:4}" "${value: -4}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-file)
      [[ $# -ge 2 ]] || fail "--env-file requires a path"
      SOURCE_ENV_FILE="$2"
      shift 2
      ;;
    --output)
      [[ $# -ge 2 ]] || fail "--output requires a path"
      OUTPUT_ENV_FILE="$2"
      shift 2
      ;;
    --strict)
      STRICT_MODE=true
      shift
      ;;
    --no-strict)
      STRICT_MODE=false
      shift
      ;;
    --apply-user-secrets)
      APPLY_USER_SECRETS=true
      shift
      ;;
    --dry-run)
      DRY_RUN=true
      shift
      ;;
    -h|--help)
      print_usage
      exit 0
      ;;
    *)
      fail "Unknown option: $1"
      ;;
  esac
done

if [[ -z "$SOURCE_ENV_FILE" ]]; then
  if [[ -f "${ROOT_DIR}/.env.local" ]]; then
    SOURCE_ENV_FILE="${ROOT_DIR}/.env.local"
  else
    SOURCE_ENV_FILE="${ROOT_DIR}/.env.example"
  fi
fi

if [[ "$OUTPUT_ENV_FILE" != /* ]]; then
  OUTPUT_ENV_FILE="${ROOT_DIR}/${OUTPUT_ENV_FILE}"
fi
if [[ "$SOURCE_ENV_FILE" != /* ]]; then
  SOURCE_ENV_FILE="${ROOT_DIR}/${SOURCE_ENV_FILE}"
fi

[[ "$OUTPUT_ENV_FILE" != "${ROOT_DIR}/.env" ]] || fail "Refusing to overwrite .env. Use .env.local instead."

read_env_file "$SOURCE_ENV_FILE"
log "Loaded seed values from ${SOURCE_ENV_FILE}"

set_effective "PSQL_CONTAINER_NAME" "charthub-postgres"
set_effective "PSQL_USER" "postgres"
set_effective "PSQL_PASSWORD" ""
set_effective "PSQL_DB" "charthub_backup"
set_effective "INTERNAL" "charthub_internal"
set_effective "DB_VOLUME" "charthub-data"
set_effective "RHYTHMVERSE_BASE_URL" "https://rhythmverse.co/"
set_effective "RHYTHMVERSE_TOKEN" ""
set_effective "BACKUP_SYNC_ENABLED" "true"
set_effective "BACKUP_SYNC_INITIAL_DELAY_MINUTES" "0"
set_effective "BACKUP_DOWNLOADS_MODE" "redirect"
set_effective "BACKUP_DOWNLOADS_EXTERNAL_REDIRECT_CACHE_HOURS" "48"
set_effective "BACKUP_DOWNLOADS_HOST_PATH" "./ChartHub.BackupApi/cache/downloads"
set_effective "BACKUP_IMAGES_HOST_PATH" "./ChartHub.BackupApi/cache/images"
set_effective "CHARTHUB_SERVER_IMAGE" "charthub-server:local"
set_effective "CHARTHUB_SERVER_CONTAINER_NAME" "charthub-server"
set_effective "CHARTHUB_SERVER_ENVIRONMENT" "Production"
set_effective "CHARTHUB_SERVER_PORT" "5180"
set_effective "CHARTHUB_SERVER_JWT_SIGNING_KEY" ""
set_effective "CHARTHUB_SERVER_JWT_ISSUER" "charthub-server"
set_effective "CHARTHUB_SERVER_JWT_AUDIENCE" "charthub-clients"
set_effective "CHARTHUB_SERVER_ALLOWED_EMAIL_0" ""
set_effective "CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY" ""
set_effective "CHARTHUB_SERVER_CONFIG_PATH" "./dev-data/config"
set_effective "CHARTHUB_SERVER_CHARTHUB_PATH" "./dev-data/charthub"
set_effective "CHARTHUB_SERVER_CLONEHERO_PATH" "./dev-data/clonehero"

set_effective "GOOGLEDRIVE_DESKTOP_CLIENT_ID" ""
set_effective "GOOGLEDRIVE_DESKTOP_CLIENT_SECRET" ""

generated_jwt=false
if is_placeholder "${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_SIGNING_KEY]}"; then
  EFFECTIVE_VALUES["CHARTHUB_SERVER_JWT_SIGNING_KEY"]="$(generate_secret)"
  generated_jwt=true
fi

generated_psql_password=false
if is_placeholder "${EFFECTIVE_VALUES[PSQL_PASSWORD]}"; then
  EFFECTIVE_VALUES["PSQL_PASSWORD"]="$(generate_secret)"
  generated_psql_password=true
fi

required_manual=(
  "RHYTHMVERSE_TOKEN"
)

if [[ "$STRICT_MODE" == true ]]; then
  missing=()
  for key in "${required_manual[@]}"; do
    if is_placeholder "${EFFECTIVE_VALUES[$key]}"; then
      missing+=("$key")
    fi
  done

  if [[ ${#missing[@]} -gt 0 ]]; then
    fail "Missing required values in strict mode: ${missing[*]}. Provide them in ${SOURCE_ENV_FILE} or as environment variables."
  fi
fi

output_body=$(cat <<EOF
# Generated by scripts/setup-local-secrets.sh
# Safe for local development. This file should not be committed.

# Docker Compose local defaults for PostgreSQL
PSQL_CONTAINER_NAME=${EFFECTIVE_VALUES[PSQL_CONTAINER_NAME]}
PSQL_USER=${EFFECTIVE_VALUES[PSQL_USER]}
PSQL_PASSWORD=${EFFECTIVE_VALUES[PSQL_PASSWORD]}
PSQL_DB=${EFFECTIVE_VALUES[PSQL_DB]}
INTERNAL=${EFFECTIVE_VALUES[INTERNAL]}
DB_VOLUME=${EFFECTIVE_VALUES[DB_VOLUME]}

# Backup API runtime settings
RHYTHMVERSE_BASE_URL=${EFFECTIVE_VALUES[RHYTHMVERSE_BASE_URL]}
RHYTHMVERSE_TOKEN=${EFFECTIVE_VALUES[RHYTHMVERSE_TOKEN]}
BACKUP_SYNC_ENABLED=${EFFECTIVE_VALUES[BACKUP_SYNC_ENABLED]}
BACKUP_SYNC_INITIAL_DELAY_MINUTES=${EFFECTIVE_VALUES[BACKUP_SYNC_INITIAL_DELAY_MINUTES]}
BACKUP_DOWNLOADS_MODE=${EFFECTIVE_VALUES[BACKUP_DOWNLOADS_MODE]}
BACKUP_DOWNLOADS_EXTERNAL_REDIRECT_CACHE_HOURS=${EFFECTIVE_VALUES[BACKUP_DOWNLOADS_EXTERNAL_REDIRECT_CACHE_HOURS]}
BACKUP_DOWNLOADS_HOST_PATH=${EFFECTIVE_VALUES[BACKUP_DOWNLOADS_HOST_PATH]}
BACKUP_IMAGES_HOST_PATH=${EFFECTIVE_VALUES[BACKUP_IMAGES_HOST_PATH]}

# ChartHub.Server runtime settings
CHARTHUB_SERVER_IMAGE=${EFFECTIVE_VALUES[CHARTHUB_SERVER_IMAGE]}
CHARTHUB_SERVER_CONTAINER_NAME=${EFFECTIVE_VALUES[CHARTHUB_SERVER_CONTAINER_NAME]}
CHARTHUB_SERVER_ENVIRONMENT=${EFFECTIVE_VALUES[CHARTHUB_SERVER_ENVIRONMENT]}
CHARTHUB_SERVER_PORT=${EFFECTIVE_VALUES[CHARTHUB_SERVER_PORT]}
CHARTHUB_SERVER_JWT_SIGNING_KEY=${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_SIGNING_KEY]}
CHARTHUB_SERVER_JWT_ISSUER=${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_ISSUER]}
CHARTHUB_SERVER_JWT_AUDIENCE=${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_AUDIENCE]}
CHARTHUB_SERVER_ALLOWED_EMAIL_0=${EFFECTIVE_VALUES[CHARTHUB_SERVER_ALLOWED_EMAIL_0]}
CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY=${EFFECTIVE_VALUES[CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY]}
CHARTHUB_SERVER_CONFIG_PATH=${EFFECTIVE_VALUES[CHARTHUB_SERVER_CONFIG_PATH]}
CHARTHUB_SERVER_CHARTHUB_PATH=${EFFECTIVE_VALUES[CHARTHUB_SERVER_CHARTHUB_PATH]}
CHARTHUB_SERVER_CLONEHERO_PATH=${EFFECTIVE_VALUES[CHARTHUB_SERVER_CLONEHERO_PATH]}

# Optional desktop Google OAuth values for ChartHub user-secrets.
GOOGLEDRIVE_DESKTOP_CLIENT_ID=${EFFECTIVE_VALUES[GOOGLEDRIVE_DESKTOP_CLIENT_ID]}
GOOGLEDRIVE_DESKTOP_CLIENT_SECRET=${EFFECTIVE_VALUES[GOOGLEDRIVE_DESKTOP_CLIENT_SECRET]}
EOF
)

if [[ "$DRY_RUN" == true ]]; then
  log "[dry-run] would write ${OUTPUT_ENV_FILE}"
else
  printf '%s\n' "$output_body" > "$OUTPUT_ENV_FILE"
  log "Wrote ${OUTPUT_ENV_FILE}"
fi

if [[ "$APPLY_USER_SECRETS" == true ]]; then
  command -v dotnet >/dev/null 2>&1 || fail "dotnet CLI is required for --apply-user-secrets"
  [[ -f "$CHART_HUB_PROJECT" ]] || fail "ChartHub project not found: $CHART_HUB_PROJECT"
  [[ -f "$SERVER_PROJECT" ]] || fail "ChartHub.Server project not found: $SERVER_PROJECT"
  [[ -f "$BACKUP_API_PROJECT" ]] || fail "ChartHub.BackupApi project not found: $BACKUP_API_PROJECT"

  ensure_user_secrets_id "$CHART_HUB_PROJECT"
  ensure_user_secrets_id "$SERVER_PROJECT"
  ensure_user_secrets_id "$BACKUP_API_PROJECT"

  set_user_secret "$CHART_HUB_PROJECT" "GoogleDrive:DesktopClientId" "${EFFECTIVE_VALUES[GOOGLEDRIVE_DESKTOP_CLIENT_ID]}"
  set_user_secret "$CHART_HUB_PROJECT" "GoogleDrive:DesktopClientSecret" "${EFFECTIVE_VALUES[GOOGLEDRIVE_DESKTOP_CLIENT_SECRET]}"

  set_user_secret "$SERVER_PROJECT" "Auth:JwtSigningKey" "${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_SIGNING_KEY]}"
  set_user_secret "$SERVER_PROJECT" "Auth:Issuer" "${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_ISSUER]}"
  set_user_secret "$SERVER_PROJECT" "Auth:Audience" "${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_AUDIENCE]}"
  set_user_secret "$SERVER_PROJECT" "Auth:AllowedEmails:0" "${EFFECTIVE_VALUES[CHARTHUB_SERVER_ALLOWED_EMAIL_0]}"
  set_user_secret "$SERVER_PROJECT" "GoogleDrive:ApiKey" "${EFFECTIVE_VALUES[CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY]}"

  backup_connection_string="Host=localhost;Port=5432;Database=${EFFECTIVE_VALUES[PSQL_DB]};Username=${EFFECTIVE_VALUES[PSQL_USER]};Password=${EFFECTIVE_VALUES[PSQL_PASSWORD]}"
  set_user_secret "$BACKUP_API_PROJECT" "Database:Provider" "postgresql"
  set_user_secret "$BACKUP_API_PROJECT" "Database:PostgreSqlConnectionString" "$backup_connection_string"
  set_user_secret "$BACKUP_API_PROJECT" "RhythmVerseSource:BaseUrl" "${EFFECTIVE_VALUES[RHYTHMVERSE_BASE_URL]}"
  set_user_secret "$BACKUP_API_PROJECT" "RhythmVerseSource:Token" "${EFFECTIVE_VALUES[RHYTHMVERSE_TOKEN]}"

  chart_hub_server_base_url="http://127.0.0.1:${EFFECTIVE_VALUES[CHARTHUB_SERVER_PORT]}"
  set_user_secret "$CHART_HUB_PROJECT" "Runtime:ServerApiBaseUrl" "$chart_hub_server_base_url"

  log "User-secrets mapping applied for ChartHub, ChartHub.Server, and ChartHub.BackupApi"
fi

log "Summary"
log "  Strict mode: ${STRICT_MODE}"
log "  Dry run: ${DRY_RUN}"
log "  Apply user-secrets: ${APPLY_USER_SECRETS}"
log "  CHARTHUB_SERVER_JWT_SIGNING_KEY: $(redact "${EFFECTIVE_VALUES[CHARTHUB_SERVER_JWT_SIGNING_KEY]}")"
log "  PSQL_PASSWORD: $(redact "${EFFECTIVE_VALUES[PSQL_PASSWORD]}")"
if [[ "$generated_jwt" == true ]]; then
  log "  Generated a new JWT signing key because seed value was missing/placeholder"
fi
if [[ "$generated_psql_password" == true ]]; then
  log "  Generated a new PostgreSQL password because seed value was missing/placeholder"
fi
log "Done"
