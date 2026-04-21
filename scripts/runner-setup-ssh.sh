#!/usr/bin/env bash
# runner-setup-ssh.sh
# Creates an ed25519 SSH deploy keypair for the ChartHub Transcription Runner
# CI deploy user, installs the public key into authorized_keys, and prints the
# private key so you can add it to GitHub Secrets.
#
# Run this once on the runner machine as root (or with sudo).
# Re-running is safe: it skips keygen if the key is already present.
#
# Usage:
#   sudo bash scripts/runner-setup-ssh.sh [--user <username>]
#
#   --user   SSH user that CI will connect as (default: calahil)

set -euo pipefail

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------
DEPLOY_USER="calahil"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --user)
            DEPLOY_USER="$2"
            shift 2
            ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

# ---------------------------------------------------------------------------
# Verify running as root
# ---------------------------------------------------------------------------
if [[ $EUID -ne 0 ]]; then
    echo "ERROR: Run this script as root (sudo)." >&2
    exit 1
fi

DEPLOY_HOME="$(getent passwd "$DEPLOY_USER" | cut -d: -f6)"
if [[ -z "$DEPLOY_HOME" ]]; then
    echo "ERROR: User '${DEPLOY_USER}' does not exist." >&2
    exit 1
fi

AUTHORIZED_KEYS="${DEPLOY_HOME}/.ssh/authorized_keys"

# ---------------------------------------------------------------------------
# Generate keypair (skip if already present)
# ---------------------------------------------------------------------------
if [[ -s "$AUTHORIZED_KEYS" ]] && grep -q "charthub-runner-deploy" "$AUTHORIZED_KEYS" 2>/dev/null; then
    echo "==> charthub-runner-deploy key already present in ${AUTHORIZED_KEYS} — skipping keygen."
    echo "    Remove the matching line from ${AUTHORIZED_KEYS} and re-run to rotate."
    echo ""
    echo "GitHub Secret: RUNNER_DEPLOY_SSH_PRIVATE_KEY = (already generated)"
    exit 0
fi

echo "==> Generating ed25519 deploy keypair..."
KEY_TMP="$(mktemp -d)"
ssh-keygen -t ed25519 -C "charthub-runner-deploy" -N "" -f "${KEY_TMP}/deploy_key" -q

PUBLIC_KEY="$(cat "${KEY_TMP}/deploy_key.pub")"
PRIVATE_KEY="$(cat "${KEY_TMP}/deploy_key")"
rm -rf "${KEY_TMP}"

# ---------------------------------------------------------------------------
# Install public key into authorized_keys
# ---------------------------------------------------------------------------
echo "==> Installing public key for user '${DEPLOY_USER}'..."
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "${DEPLOY_HOME}/.ssh"
printf '%s\n' "$PUBLIC_KEY" >> "$AUTHORIZED_KEYS"
chown "${DEPLOY_USER}:${DEPLOY_USER}" "$AUTHORIZED_KEYS"
chmod 600 "$AUTHORIZED_KEYS"

echo "==> Public key installed to: ${AUTHORIZED_KEYS}"

# ---------------------------------------------------------------------------
# Ensure openssh-server is installed and running
# ---------------------------------------------------------------------------
if ! command -v sshd &>/dev/null; then
    echo "==> Installing openssh-server..."
    apt-get install -y --no-install-recommends openssh-server
fi
systemctl enable --now ssh

# ---------------------------------------------------------------------------
# Print private key for GitHub Secrets
# ---------------------------------------------------------------------------
echo ""
echo "========================================================================"
echo " RUNNER DEPLOY SSH SETUP COMPLETE"
echo "========================================================================"
echo ""
echo " Add the following to your GitHub Environment secrets:"
echo ""
echo "   RUNNER_DEPLOY_SSH_HOST  = $(hostname -I | awk '{print $1}') (or your Tailscale IP)"
echo "   RUNNER_DEPLOY_SSH_USER  = ${DEPLOY_USER}"
echo "   RUNNER_DEPLOY_SSH_PORT  = 22  (omit if default)"
echo ""
echo "   RUNNER_DEPLOY_SSH_PRIVATE_KEY ="
echo "------------------------------------------------------------------------"
printf '%s\n' "$PRIVATE_KEY"
echo "------------------------------------------------------------------------"
echo ""
echo " Copy the private key (including BEGIN/END lines) into the GitHub Secret."
echo "========================================================================"
