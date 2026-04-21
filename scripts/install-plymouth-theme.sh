#!/usr/bin/env bash
# install-plymouth-theme.sh
# Installs the ChartHub boot splash (Plymouth script theme).
#
# Designed to be called from kiosk-setup.sh (runs as root in a chroot or on
# a live Ubuntu 24.04 system) or manually:
#
#   sudo bash scripts/install-plymouth-theme.sh
#
# What it does:
#   1. Installs plymouth, plymouth-themes, imagemagick
#   2. Processes the ChartHub logo (strips white background, resizes to 300 px)
#   3. Generates the pill-shaped bar track and faded highlight PNGs
#   4. Copies the theme script and manifest into place
#   5. Registers + activates the theme
#   6. Ensures GRUB passes `quiet splash` at boot
#   7. Rebuilds the initramfs so the theme is embedded
#
# Colors (Catppuccin Macchiato):
#   Background : #24273A  (Base)
#   Bar track  : #363A4F  (Surface0)
#   Highlight  : #C6A0F6  (Mauve)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

THEME_NAME="charthub"
THEME_DIR="/usr/share/plymouth/themes/${THEME_NAME}"
PLYMOUTH_SRC="${SCRIPT_DIR}/plymouth"

# logo-1024.png (full-colour, white background) lives in the Hud project
LOGO_SRC="${SCRIPT_DIR}/../ChartHub.Hud/Resources/logo-1024.png"

# ---------------------------------------------------------------------------
# 1. Verify running as root
# ---------------------------------------------------------------------------
if [[ $EUID -ne 0 ]]; then
    echo "ERROR: This script must be run as root (sudo)." >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# 2. Install dependencies
# ---------------------------------------------------------------------------
echo "==> Installing plymouth and imagemagick..."
apt-get install -y \
    plymouth \
    plymouth-themes \
    imagemagick

if ! find /usr/lib -path '*/plymouth/script.so' -print -quit | grep -q .; then
    echo "ERROR: Plymouth script engine was not found after installation." >&2
    echo "       Expected a script.so plugin under /usr/lib/*/plymouth/." >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# 3. Create theme directory
# ---------------------------------------------------------------------------
echo "==> Creating theme directory at ${THEME_DIR}..."
mkdir -p "${THEME_DIR}"

# ---------------------------------------------------------------------------
# 4. Process logo
#    - Strip the white background using colour-fuzz tolerance
#    - Resize to 300 × 300 px (logo is square, so aspect ratio is preserved)
#
#    NOTE: This approach works well for the ChartHub logo because it uses a
#    clean white background with dark-coloured artwork. If the logo is ever
#    replaced with one that has a transparent background, remove the
#    -fuzz/-transparent step.
# ---------------------------------------------------------------------------
echo "==> Processing logo (strip white background, resize to 300 px)..."
convert "${LOGO_SRC}" \
    -fuzz 5% \
    -transparent white \
    -trim \
    +repage \
    -resize '300x300' \
    "${THEME_DIR}/logo.png"

# ---------------------------------------------------------------------------
# 5. Generate pill-shaped bar track
#    Size: 300 × 10 px  — Macchiato Surface0 (#363A4F)  — corner radius 5 px
# ---------------------------------------------------------------------------
echo "==> Generating bar track PNG..."
convert -size 300x10 xc:none \
    -fill '#363A4F' \
    -draw 'roundrectangle 0,0 299,9 5,5' \
    "${THEME_DIR}/track.png"

# ---------------------------------------------------------------------------
# 6. Generate faded highlight
#    Size: 120 × 8 px  — Macchiato Mauve (#C6A0F6)
#    A horizontal gradient (transparent → mauve → transparent) blends cleanly
#    as the highlight slides on and off the ends of the pill track.
#
#    Strategy (ImageMagick 6, Ubuntu 24.04):
#      Left  half (60 × 8):  gradient mask white→black, then negate → fade-in
#      Right half (60 × 8):  gradient mask white→black              → fade-out
#    Both halves use the mask as the alpha channel of a solid mauve rectangle,
#    then the two halves are appended horizontally.
# ---------------------------------------------------------------------------
echo "==> Generating sliding highlight PNG..."

# Left half: transparent on the left, fully opaque on the right
convert -size 8x60 gradient: \
    -rotate 90 \
    -negate \
    \( -size 60x8 xc:'#C6A0F6' \) \
    -compose CopyOpacity \
    -composite \
    /tmp/_ch_hi_left.png

# Right half: fully opaque on the left, transparent on the right
convert -size 8x60 gradient: \
    -rotate 90 \
    \( -size 60x8 xc:'#C6A0F6' \) \
    -compose CopyOpacity \
    -composite \
    /tmp/_ch_hi_right.png

convert /tmp/_ch_hi_left.png /tmp/_ch_hi_right.png \
    +append \
    "${THEME_DIR}/highlight.png"

rm -f /tmp/_ch_hi_left.png /tmp/_ch_hi_right.png

# ---------------------------------------------------------------------------
# 7. Copy Plymouth theme files
# ---------------------------------------------------------------------------
echo "==> Copying theme files..."
install -m 0644 "${PLYMOUTH_SRC}/charthub.plymouth" "${THEME_DIR}/charthub.plymouth"
install -m 0644 "${PLYMOUTH_SRC}/charthub.script"   "${THEME_DIR}/charthub.script"

# ---------------------------------------------------------------------------
# 8. Register and activate the theme
# ---------------------------------------------------------------------------
echo "==> Registering theme with update-alternatives..."
update-alternatives --install \
    /usr/share/plymouth/themes/default.plymouth \
    default.plymouth \
    "${THEME_DIR}/charthub.plymouth" \
    100

# Force-select this theme alternative to avoid stale default selection.
update-alternatives --set default.plymouth "${THEME_DIR}/charthub.plymouth"

echo "==> Setting charthub as the default Plymouth theme..."
# Write plymouthd.conf directly — more reliable than calling
# plymouth-set-default-theme, which is in /usr/sbin and may not be in PATH
# during a root setup script on Ubuntu Server.
mkdir -p /etc/plymouth
cat > /etc/plymouth/plymouthd.conf <<'PLYMOUTH_CONF_EOF'
[Daemon]
Theme=charthub
ShowDelay=0
PLYMOUTH_CONF_EOF

# ---------------------------------------------------------------------------
# 9. Ensure GRUB passes 'quiet splash' so Plymouth is actually displayed.
#    The kiosk machine boots headless non-interactively, so we append the
#    parameters safely if they are not already present.
# ---------------------------------------------------------------------------
GRUB_DEFAULT_FILE="/etc/default/grub"
if [[ -f "${GRUB_DEFAULT_FILE}" ]]; then
    echo "==> Ensuring GRUB_CMDLINE_LINUX_DEFAULT contains 'quiet splash'..."

    # Extract the current value
    current_cmdline="$(grep -oP '(?<=GRUB_CMDLINE_LINUX_DEFAULT=")[^"]*' "${GRUB_DEFAULT_FILE}" || true)"

    needs_update=0
    new_cmdline="${current_cmdline}"

    if [[ "${new_cmdline}" != *"quiet"* ]]; then
        new_cmdline="${new_cmdline} quiet"
        needs_update=1
    fi
    if [[ "${new_cmdline}" != *"splash"* ]]; then
        new_cmdline="${new_cmdline} splash"
        needs_update=1
    fi

    if [[ $needs_update -eq 1 ]]; then
        # Collapse any run of multiple spaces to one
        new_cmdline="$(echo "${new_cmdline}" | tr -s ' ' | sed 's/^ //')"
        sed -i "s|GRUB_CMDLINE_LINUX_DEFAULT=\"[^\"]*\"|GRUB_CMDLINE_LINUX_DEFAULT=\"${new_cmdline}\"|" \
            "${GRUB_DEFAULT_FILE}"
        update-grub
    else
        echo "    GRUB_CMDLINE_LINUX_DEFAULT already contains 'quiet splash' — skipping."
    fi
else
    echo "    WARNING: ${GRUB_DEFAULT_FILE} not found — skipping GRUB update."
    echo "             Ensure your bootloader passes 'quiet splash' manually."
fi

# ---------------------------------------------------------------------------
# 10. Rebuild initramfs to embed the theme
#     This is required for the splash to appear during early boot.
# ---------------------------------------------------------------------------
echo "==> Rebuilding initramfs (this may take a moment)..."
update-initramfs -u -k all

echo ""
echo "==> ChartHub Plymouth theme installed and activated."
echo "    Reboot to see the splash screen."
