#!/usr/bin/env bash
# sync-github-secrets.sh
# Reads secrets from Infisical (or a fallback env file) and pushes them to all
# GitHub repository environments using the `gh` CLI.
#
# Usage:
#   scripts/sync-github-secrets.sh [options]
#
# Options:
#   --env-file <path>     Read secrets from this env file instead of Infisical.
#   --environment <name>  Sync only this GitHub environment (dev|staging|production).
#                         Default: sync all three.
#   --dry-run             Print what would be set without writing anything.
#   --infisical-project   Infisical project slug (overrides INFISICAL_PROJECT_ID env var).
#   -h, --help            Show usage.
#
# Requirements:
#   - gh CLI authenticated (gh auth login)
#   - infisical CLI installed and authenticated (infisical login) when using Infisical mode
#
# Environment variables:
#   INFISICAL_PROJECT_ID   Infisical project ID (from project settings)
#   INFISICAL_HOST         Infisical host URL (default: https://app.infisical.com — set to your self-hosted URL)

set -euo pipefail

REPO="calahil/ChartHub"
ALL_ENVIRONMENTS=(dev staging production)
ALL_SERVER_ENVIRONMENTS=(server-dev server-staging server-production)
TARGET_ENVIRONMENTS=()
TARGET_SERVER_ENVIRONMENTS=()
ENV_FILE=""
DRY_RUN=false
INFISICAL_PROJECT="${INFISICAL_PROJECT_ID:-}"
INFISICAL_HOST_URL="${INFISICAL_HOST:-}"

# Secrets to sync per GitHub environment.
# Maps GitHub environment name → Infisical environment slug.
declare -A INFISICAL_ENV_MAP=(
  [dev]="dev"
  [staging]="staging"
  [production]="production"
  [server-dev]="server-dev"
  [server-staging]="server-staging"
  [server-production]="server-production"
)

# The complete set of secrets each GitHub BackupApi environment needs.
COMMON_SECRETS=(
  PSQL_CONTAINER_NAME
  PSQL_PORT
  PSQL_USER
  PSQL_PASSWORD
  PSQL_DB
  INTERNAL
  API_PORT
  BACKUP_API_CONTAINER_NAME
  BACKUP_DOWNLOADS_HOST_PATH
  BACKUP_IMAGES_HOST_PATH
  RHYTHMVERSE_TOKEN
  BACKUP_SYNC_ENABLED
  DB_VOLUME
  BACKUP_API_KEY
  BACKUP_API_SSH_HOST
  BACKUP_API_SSH_USER
  BACKUP_API_SSH_PRIVATE_KEY
  BACKUP_API_SSH_PORT
)

# The complete set of secrets each GitHub ChartHub.Server environment needs.
SERVER_SECRETS=(
  CHARTHUB_SERVER_JWT_SIGNING_KEY
  CHARTHUB_SERVER_JWT_ISSUER
  CHARTHUB_SERVER_JWT_AUDIENCE
  CHARTHUB_SERVER_JWT_ALLOWED_EMAILS
  CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES
  CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY
  CHARTHUB_SERVER_CHARTHUB_PORT
  CHARTHUB_SERVER_CONFIG_PATH
  CHARTHUB_SERVER_DATA_PATH
  CHARTHUB_SERVER_DOWNLOADS_PATH
  CHARTHUB_SERVER_STAGING_PATH
  CHARTHUB_SERVER_CLONEHERO_PATH
  CHARTHUB_SERVER_DB_PATH
  CHARTHUB_SERVER_LOGS_PATH
  SERVER_DEPLOY_SSH_HOST
  SERVER_DEPLOY_SSH_USER
  SERVER_DEPLOY_SSH_PRIVATE_KEY
  SERVER_DEPLOY_SSH_PORT
)

# Repository-level secrets (not scoped to any environment).
# These are read from the Infisical 'dev' environment and pushed as repo secrets.
REPO_SECRETS=(
  TAILSCALE_OAUTH_CLIENT_ID
  TAILSCALE_OAUTH_CLIENT_SECRET
  GOOGLEDRIVE_ANDROID_CLIENT_ID
  GOOGLEDRIVE_DESKTOP_CLIENT_ID
  GOOGLEDRIVE_DESKTOP_CLIENT_SECRET
)

print_usage() {
  cat <<'EOF'
Usage: scripts/sync-github-secrets.sh [options]

Pushes ChartHub secrets to GitHub repository environments.

Options:
  --env-file <path>            Read from this env file instead of Infisical CLI.
  --environment <name>         Sync only this environment (dev|staging|production).
  --dry-run                    Print secrets that would be set without writing.
  --infisical-project <id>     Infisical project ID (overrides INFISICAL_PROJECT_ID).
  -h, --help                   Show this help.

Examples:
  # Sync all environments from Infisical
  INFISICAL_PROJECT_ID=abc123 scripts/sync-github-secrets.sh

  # Sync only production from a local file
  scripts/sync-github-secrets.sh --env-file .env.production --environment production

  # Preview what would be synced
  scripts/sync-github-secrets.sh --dry-run
EOF
}

log()  { printf '[sync-github-secrets] %s\n' "$1"; }
fail() { printf '[sync-github-secrets] ERROR: %s\n' "$1" >&2; exit 1; }

declare -A LOADED_SECRETS

load_from_env_file() {
  local file="$1"
  [[ -f "$file" ]] || fail "env file not found: $file"
  log "Loading secrets from ${file}"

  local line key value
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "${line// }" ]] && continue
    [[ "${line#\#}" != "$line" ]] && continue
    [[ "$line" != *=* ]] && continue
    key="${line%%=*}"
    value="${line#*=}"
    key="${key%%[[:space:]]*}"
    [[ -z "$key" ]] && continue
    if [[ "$value" =~ ^\".*\"$ ]]; then value="${value:1:${#value}-2}"; fi
    if [[ "$value" =~ ^\'.*\'$ ]]; then value="${value:1:${#value}-2}"; fi
    LOADED_SECRETS["$key"]="$value"
  done < "$file"
}

load_from_infisical() {
  local infisical_env="$1"

  command -v infisical >/dev/null 2>&1 || fail "infisical CLI not found. Install from https://infisical.com/docs/cli/overview"
  [[ -n "$INFISICAL_PROJECT" ]] || fail "INFISICAL_PROJECT_ID is not set. Export it or pass --infisical-project."

  local host_flag=()
  if [[ -n "$INFISICAL_HOST_URL" ]]; then
    host_flag=(--domain "$INFISICAL_HOST_URL")
  fi

  log "Fetching secrets from Infisical (env: ${infisical_env}, project: ${INFISICAL_PROJECT})"

  local raw_env
  raw_env=$(infisical export \
    --projectId "$INFISICAL_PROJECT" \
    --env "$infisical_env" \
    --format dotenv \
    "${host_flag[@]}" 2>/dev/null) || fail "infisical export failed for environment '${infisical_env}'. Are you logged in? Run: infisical login"

  # Clear previously loaded secrets for this environment pass
  for k in "${!LOADED_SECRETS[@]}"; do unset "LOADED_SECRETS[$k]"; done

  local line key value
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "${line// }" ]] && continue
    [[ "${line#\#}" != "$line" ]] && continue
    [[ "$line" != *=* ]] && continue
    key="${line%%=*}"
    value="${line#*=}"
    key="${key%%[[:space:]]*}"
    [[ -z "$key" ]] && continue
    if [[ "$value" =~ ^\".*\"$ ]]; then value="${value:1:${#value}-2}"; fi
    if [[ "$value" =~ ^\'.*\'$ ]]; then value="${value:1:${#value}-2}"; fi
    LOADED_SECRETS["$key"]="$value"
  done <<< "$raw_env"
}

push_to_github_repo() {
  local -n secrets_ref="$1"

  log "Syncing → GitHub repository secrets (repo-level)"

  local missing=()
  for secret in "${secrets_ref[@]}"; do
    if [[ -z "${LOADED_SECRETS[$secret]+x}" || -z "${LOADED_SECRETS[$secret]}" ]]; then
      missing+=("$secret")
    fi
  done

  if [[ ${#missing[@]} -gt 0 ]]; then
    fail "Missing repo secrets: ${missing[*]}"
  fi

  for secret in "${secrets_ref[@]}"; do
    local value="${LOADED_SECRETS[$secret]}"
    if [[ "$DRY_RUN" == true ]]; then
      log "  [dry-run] gh secret set ${secret} --repo (repo-level)"
    else
      printf '%s' "$value" | gh secret set "$secret" \
        --repo "$REPO" \
        --body -
      log "  ✓ ${secret}"
    fi
  done
}

push_to_github_environment() {
  local github_env="$1"
  local -n secrets_ref="$2"

  log "Syncing → GitHub environment: ${github_env}"

  local missing=()
  for secret in "${secrets_ref[@]}"; do
    if [[ -z "${LOADED_SECRETS[$secret]+x}" || -z "${LOADED_SECRETS[$secret]}" ]]; then
      missing+=("$secret")
    fi
  done

  if [[ ${#missing[@]} -gt 0 ]]; then
    fail "Missing secrets for environment '${github_env}': ${missing[*]}"
  fi

  for secret in "${secrets_ref[@]}"; do
    local value="${LOADED_SECRETS[$secret]}"
    if [[ "$DRY_RUN" == true ]]; then
      log "  [dry-run] gh secret set ${secret} --env ${github_env}"
    else
      printf '%s' "$value" | gh secret set "$secret" \
        --repo "$REPO" \
        --env "$github_env" \
        --body -
      log "  ✓ ${secret}"
    fi
  done
}

# --- Argument parsing ---
while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-file)
      [[ $# -ge 2 ]] || fail "--env-file requires a path"
      ENV_FILE="$2"; shift 2 ;;
    --environment)
      [[ $# -ge 2 ]] || fail "--environment requires a name"
      # Route to correct list based on env name prefix
      if [[ "$2" == server-* ]]; then
        TARGET_SERVER_ENVIRONMENTS+=("$2")
      else
        TARGET_ENVIRONMENTS+=("$2")
      fi
      shift 2 ;;
    --infisical-project)
      [[ $# -ge 2 ]] || fail "--infisical-project requires an ID"
      INFISICAL_PROJECT="$2"; shift 2 ;;
    --dry-run)
      DRY_RUN=true; shift ;;
    -h|--help)
      print_usage; exit 0 ;;
    *)
      fail "Unknown option: $1" ;;
  esac
done

if [[ ${#TARGET_ENVIRONMENTS[@]} -eq 0 && ${#TARGET_SERVER_ENVIRONMENTS[@]} -eq 0 ]]; then
  TARGET_ENVIRONMENTS=("${ALL_ENVIRONMENTS[@]}")
  TARGET_SERVER_ENVIRONMENTS=("${ALL_SERVER_ENVIRONMENTS[@]}")
fi

# Validate requested environments
for env in "${TARGET_ENVIRONMENTS[@]}"; do
  [[ " ${ALL_ENVIRONMENTS[*]} " == *" ${env} "* ]] || \
    fail "Unknown environment '${env}'. Valid values: ${ALL_ENVIRONMENTS[*]} ${ALL_SERVER_ENVIRONMENTS[*]}"
done
for env in "${TARGET_SERVER_ENVIRONMENTS[@]}"; do
  [[ " ${ALL_SERVER_ENVIRONMENTS[*]} " == *" ${env} "* ]] || \
    fail "Unknown environment '${env}'. Valid values: ${ALL_ENVIRONMENTS[*]} ${ALL_SERVER_ENVIRONMENTS[*]}"
done

command -v gh >/dev/null 2>&1 || fail "gh CLI not found. Install from https://cli.github.com"
gh auth status --hostname github.com >/dev/null 2>&1 || fail "Not authenticated with gh. Run: gh auth login"

# --- Sync ---
if [[ -n "$ENV_FILE" ]]; then
  # Single env file mode: same values pushed to all targeted environments
  load_from_env_file "$ENV_FILE"
  push_to_github_repo REPO_SECRETS
  for github_env in "${TARGET_ENVIRONMENTS[@]}"; do
    push_to_github_environment "$github_env" COMMON_SECRETS
  done
  for github_env in "${TARGET_SERVER_ENVIRONMENTS[@]}"; do
    push_to_github_environment "$github_env" SERVER_SECRETS
  done
else
  # Infisical mode: repo secrets sourced from 'dev' (build-time, not deployment-specific)
  load_from_infisical "dev"
  push_to_github_repo REPO_SECRETS
  # Per-environment secrets
  for github_env in "${TARGET_ENVIRONMENTS[@]}"; do
    infisical_env="${INFISICAL_ENV_MAP[$github_env]}"
    load_from_infisical "$infisical_env"
    push_to_github_environment "$github_env" COMMON_SECRETS
  done
  for github_env in "${TARGET_SERVER_ENVIRONMENTS[@]}"; do
    infisical_env="${INFISICAL_ENV_MAP[$github_env]}"
    load_from_infisical "$infisical_env"
    push_to_github_environment "$github_env" SERVER_SECRETS
  done
fi

log "Sync complete."
if [[ "$DRY_RUN" == true ]]; then
  log "Dry run — no secrets were written."
fi
