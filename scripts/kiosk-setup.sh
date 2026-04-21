#!/usr/bin/env bash
# kiosk-setup.sh
# Configures an Ubuntu Server 24.04 LTS minimal install to run ChartHub.Server
# as a kiosk with Openbox + LightDM autologin.
#
# Designed to run either:
#   (a) Inside Cubic's chroot before ISO export — produces a fully baked image
#   (b) On a freshly installed Ubuntu Server VM or bare-metal machine
#
# What this script does:
#   - Removes server-specific packages not needed at runtime (cloud-init, snapd,
#     LXD, iSCSI, multipath, crash reporters, unattended-upgrades, etc.)
#   - Installs X11 + LightDM + Openbox + AMD Mesa/GPU stack + audio stack
#   - Installs openssh-server, unzip, dotnet-runtime-10.0, Tailscale
#   - Creates 'gamer' user with a generated SSH deploy keypair baked in
#   - Creates /srv/appdata/charthub/{prod,staging,dev}/ directory trees
#   - Sets active → prod symlink (CI flips this on VM for dev/staging)
#   - Installs and enables charthub-server.service
#   - Configures LightDM autologin into the charthub-kiosk X session
#
# ---------------------------------------------------------------------------
# Reference hardware (emubox — production kiosk):
#
#   OS:     Ubuntu Server 24.04.3 LTS (Noble Numbat) — minimal install
#   CPU:    AMD A9-9400 Radeon R5, 5 Compute Cores 2C+3G @ 2.40 GHz (2 threads)
#   GPU:    AMD Radeon R5 Graphics (Bristol Ridge, GCN 1.2 integrated APU)
#           — no discrete GPU; Mesa radeonsi is the only OpenGL/Vulkan stack
#   RAM:    8 GB DDR4
#   Audio:  USB microphone + USB e-drum kit (MIDI) + Xbox 360 wireless receiver
#   Games:  YARG, Clone Hero (Unity/SDL2, require X11 WM + Mesa + PulseAudio)
#
# If your hardware differs (e.g. NVIDIA GPU):
#   - Section 6: swap amdgpu/radeon packages for nvidia equivalents
#   - Section 6: remove linux-firmware if nvidia driver ships its own firmware
# ---------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SESSION_SCRIPT="/usr/local/bin/start-charthub-kiosk"
SESSION_DESKTOP="/usr/share/xsessions/charthub-kiosk.desktop"
LIGHTDM_CONF="/etc/lightdm/lightdm.conf.d/99-charthub-kiosk.conf"

# ---------------------------------------------------------------------------
# 1. Verify running as root
# ---------------------------------------------------------------------------
if [[ $EUID -ne 0 ]]; then
    echo "ERROR: This script must be run as root (sudo)." >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# 2. Generate ed25519 deploy keypair
#    Public key → /home/gamer/.ssh/authorized_keys (baked into image)
#    Private key → printed at end for operator to add to GitHub Secrets
#
#    Skipped if /home/gamer/.ssh/authorized_keys already contains a key —
#    re-running the script will not rotate credentials already in GitHub Secrets.
# ---------------------------------------------------------------------------
EXISTING_AUTHORIZED_KEYS="/home/gamer/.ssh/authorized_keys"
if [[ -s "$EXISTING_AUTHORIZED_KEYS" ]]; then
    echo "==> SSH keypair already exists in ${EXISTING_AUTHORIZED_KEYS} — skipping keygen."
    echo "    Delete ${EXISTING_AUTHORIZED_KEYS} and re-run to rotate credentials."
    DEPLOY_SSH_PUBLIC_KEY="$(cat "$EXISTING_AUTHORIZED_KEYS")"
    DEPLOY_SSH_PRIVATE_KEY="(already generated — check your GitHub Secrets)"
else
    _KEY_TMP="$(mktemp -d)"
    ssh-keygen -t ed25519 -C "charthub-deploy" -N "" -f "${_KEY_TMP}/deploy_key" -q
    DEPLOY_SSH_PUBLIC_KEY="$(cat "${_KEY_TMP}/deploy_key.pub")"
    DEPLOY_SSH_PRIVATE_KEY="$(cat "${_KEY_TMP}/deploy_key")"
    rm -rf "${_KEY_TMP}"
fi

# ---------------------------------------------------------------------------
# 3. Refresh package lists
# ---------------------------------------------------------------------------
echo "==> Refreshing package lists..."
apt-get update -q

# ---------------------------------------------------------------------------
# 4. Remove server-specific packages not needed at runtime
# ---------------------------------------------------------------------------

echo "==> Removing snapd..."
apt-get remove -y --auto-remove snapd || true
cat > /etc/apt/preferences.d/no-snap <<'SNAP_EOF'
Package: snapd
Pin: release a=*
Pin-Priority: -1
SNAP_EOF

echo "==> Removing cloud-init and cloud tools..."
apt-get remove -y --auto-remove cloud-init cloud-guest-utils || true
mkdir -p /etc/cloud/cloud.cfg.d
echo 'datasource_list: [ None ]' > /etc/cloud/cloud.cfg.d/99-disable.cfg

echo "==> Removing LXD installer..."
apt-get remove -y --auto-remove lxd-installer || true

echo "==> Removing iSCSI and multipath (server storage stack)..."
apt-get remove -y --auto-remove \
    open-iscsi \
    multipath-tools \
    kpartx \
    sg3-utils \
    sg3-utils-udev \
    || true

echo "==> Removing LVM and device-mapper extras..."
apt-get remove -y --auto-remove \
    lvm2 \
    dmeventd \
    thin-provisioning-tools \
    || true

echo "==> Removing software RAID (mdadm)..."
apt-get remove -y --auto-remove mdadm || true

echo "==> Removing extra filesystems (btrfs, xfs)..."
apt-get remove -y --auto-remove \
    btrfs-progs \
    xfsprogs \
    || true

echo "==> Removing PackageKit and update management tools..."
apt-get remove -y --auto-remove \
    packagekit \
    packagekit-tools \
    software-properties-common \
    ubuntu-release-upgrader-core \
    ubuntu-drivers-common \
    unattended-upgrades \
    || true

echo "==> Removing crash reporters and telemetry..."
apt-get remove -y --auto-remove \
    apport \
    apport-symptoms \
    apport-core-dump-handler \
    python3-apport \
    pollinate \
    || true

echo "==> Removing needrestart..."
apt-get remove -y --auto-remove needrestart || true

echo "==> Removing eatmydata (install-time only)..."
apt-get remove -y --auto-remove eatmydata || true

# ---------------------------------------------------------------------------
# 5. Install X11 display stack
# ---------------------------------------------------------------------------
echo "==> Installing X11 display stack..."
apt-get install -y \
    xorg \
    xserver-xorg \
    xserver-xorg-core \
    xserver-xorg-input-all \
    x11-xserver-utils \
    xinit

# ---------------------------------------------------------------------------
# 6. Install AMD GPU drivers and Mesa stack
#    Mesa radeonsi is the only OpenGL/Vulkan path for AMD GCN integrated APUs.
#    linux-firmware contains the GPU microcode blobs required at boot.
# ---------------------------------------------------------------------------
echo "==> Installing AMD GPU drivers and Mesa stack..."
apt-get install -y \
    xserver-xorg-video-amdgpu \
    xserver-xorg-video-radeon \
    libgl1-mesa-dri \
    libglx-mesa0 \
    libgles2 \
    mesa-vulkan-drivers \
    libvulkan1 \
    libdrm2 \
    libdrm-amdgpu1 \
    libdrm-radeon1 \
    linux-firmware

apt-mark manual \
    xserver-xorg-video-amdgpu \
    xserver-xorg-video-radeon \
    libgl1-mesa-dri \
    libglx-mesa0 \
    libgles2 \
    mesa-vulkan-drivers \
    libvulkan1 \
    libdrm2 \
    libdrm-amdgpu1 \
    libdrm-radeon1 \
    linux-firmware

# ---------------------------------------------------------------------------
# 7. Install LightDM and Openbox
# ---------------------------------------------------------------------------
echo "==> Installing LightDM and Openbox..."
apt-get install -y \
    lightdm \
    lightdm-gtk-greeter \
    openbox

# Ensure LightDM is selected as the display manager in non-interactive installs.
echo "==> Ensuring LightDM is the default display manager..."
mkdir -p /etc/X11
printf '/usr/sbin/lightdm\n' > /etc/X11/default-display-manager
if [[ -d /run/systemd/system ]]; then
    systemctl enable lightdm
else
    systemctl --root / enable lightdm
fi

# ---------------------------------------------------------------------------
# 8. Install audio stack
#    PulseAudio is required by Unity/SDL2 games and USB audio devices.
#    alsa-utils provides aplay/amixer for MIDI and USB audio configuration.
# ---------------------------------------------------------------------------
echo "==> Installing audio stack..."
apt-get install -y \
    pulseaudio \
    pulseaudio-utils \
    alsa-utils \
    libasound2t64
apt-mark manual pulseaudio pulseaudio-utils alsa-utils libasound2t64

# ---------------------------------------------------------------------------
# 9. Install Bluetooth stack (game controllers: Xbox 360 receiver, etc.)
# ---------------------------------------------------------------------------
echo "==> Installing Bluetooth stack..."
apt-get install -y \
    bluez \
    bluez-tools
apt-mark manual bluez bluez-tools

# ---------------------------------------------------------------------------
# 10. Install media codecs
# ---------------------------------------------------------------------------
echo "==> Installing ffmpeg..."
apt-get install -y ffmpeg
apt-mark manual ffmpeg

# ---------------------------------------------------------------------------
# 11. Install SSH server and deploy tools
# ---------------------------------------------------------------------------
echo "==> Installing openssh-server and unzip..."
apt-get install -y \
    openssh-server \
    unzip
apt-mark manual openssh-server unzip

# ---------------------------------------------------------------------------
# 12. Install .NET runtimes
#     ChartHub.Server targets net10.0; 8.0 retained as fallback.
# ---------------------------------------------------------------------------
echo "==> Installing .NET runtimes (8.0 + 10.0)..."
if [[ ! -f /etc/apt/sources.list.d/microsoft-prod.list ]]; then
    curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
        -o /tmp/packages-microsoft-prod.deb
    dpkg -i /tmp/packages-microsoft-prod.deb
    rm -f /tmp/packages-microsoft-prod.deb
    apt-get update -q
fi
apt-get install -y \
    dotnet-runtime-8.0 \
    aspnetcore-runtime-8.0 \
    dotnet-runtime-10.0 \
    aspnetcore-runtime-10.0
apt-mark manual \
    dotnet-runtime-8.0 \
    aspnetcore-runtime-8.0 \
    dotnet-runtime-10.0 \
    aspnetcore-runtime-10.0

# ---------------------------------------------------------------------------
# 13. Install Tailscale
#     After first boot: sudo tailscale up --authkey <key>
# ---------------------------------------------------------------------------
echo "==> Installing Tailscale..."
curl -fsSL https://tailscale.com/install.sh | sh
if [[ -d /run/systemd/system ]]; then
    systemctl enable tailscaled
else
    systemctl --root / enable tailscaled
fi

# ---------------------------------------------------------------------------
# 14. Final autoremove pass
# ---------------------------------------------------------------------------
echo "==> Final autoremove..."
apt-get autoremove -y --purge
apt-get clean

# ---------------------------------------------------------------------------
# 15. Create 'gamer' user (CI deploy user; owns all appdata)
# ---------------------------------------------------------------------------
echo "==> Creating 'gamer' user..."
groupadd -f autologin
groupadd -f nopasswdlogin
if ! id gamer &>/dev/null; then
    useradd -m -s /bin/bash -G sudo,autologin,nopasswdlogin,audio,video,input,bluetooth,plugdev,dialout gamer
    passwd -l gamer
fi
mkdir -p /home/gamer/.ssh
chown gamer:gamer /home/gamer/.ssh
chmod 700 /home/gamer/.ssh
if [[ ! -s /home/gamer/.ssh/authorized_keys ]]; then
    printf '%s\n' "${DEPLOY_SSH_PUBLIC_KEY}" > /home/gamer/.ssh/authorized_keys
fi
chown gamer:gamer /home/gamer/.ssh/authorized_keys
chmod 600 /home/gamer/.ssh/authorized_keys

# ---------------------------------------------------------------------------
# 16. Create /srv/appdata/charthub/{prod,staging,dev}/ directory trees
#     CI deploys into these trees; binary slot (current/) is created by CI.
#     prod/ is the default active env (emubox). dev/ and staging/ are for the VM.
# ---------------------------------------------------------------------------
_SUBDIRS=(
    config
    data/downloads
    data/staging/install
    data/staging/jobs
    data/staging/onyx
    db
    logs
    music
    releases
)

echo "==> Creating /srv/appdata/charthub/{prod,staging,dev}/ directory trees..."
for ENV in prod staging dev; do
    for SUBDIR in "${_SUBDIRS[@]}"; do
        mkdir -p "/srv/appdata/charthub/${ENV}/${SUBDIR}"
    done
done

# 'active' points at prod by default.
# On the VM, CI will flip this symlink to 'dev' or 'staging' on each deploy.
ln -sfn /srv/appdata/charthub/prod /srv/appdata/charthub/active

chown -R gamer:gamer /srv/appdata/charthub

# ---------------------------------------------------------------------------
# 17. Install and enable charthub-server.service
#     The binary does not exist yet — CI provides it on first deploy.
#     systemd will fail to start until the binary is deployed; that is expected.
# ---------------------------------------------------------------------------
echo "==> Installing charthub-server.service..."
cat > /etc/systemd/system/charthub-server.service <<'SERVICE_EOF'
[Unit]
Description=ChartHub Server
After=network-online.target graphical.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=/srv/appdata/charthub/active/current
EnvironmentFile=/srv/appdata/charthub/active/config/charthub-server.env
Environment=XDG_RUNTIME_DIR=/run/user/1000
Environment=DISPLAY=:0
Environment=WAYLAND_DISPLAY=wayland-0
Environment=PULSE_SERVER=unix:/run/user/1000/pulse/native
ExecStart=/srv/appdata/charthub/active/current/ChartHub.Server
Restart=always
RestartSec=5
User=gamer
Group=gamer

[Install]
WantedBy=multi-user.target
SERVICE_EOF
chmod 0644 /etc/systemd/system/charthub-server.service
if [[ -d /run/systemd/system ]]; then
    systemctl daemon-reload
    systemctl enable charthub-server.service
else
    systemctl --root / daemon-reload 2>/dev/null || true
    systemctl --root / enable charthub-server.service
fi
echo "    NOTE: Start will fail until CI deploys the binary — this is expected."

# ---------------------------------------------------------------------------
# 18. udev rule: grant input group read/write access to /dev/uinput
#     Required for ChartHub.Server to create virtual gamepads via uinput.
# ---------------------------------------------------------------------------
echo "==> Installing uinput udev rule..."
echo 'KERNEL=="uinput", GROUP="input", MODE="0660"' > /etc/udev/rules.d/99-uinput.rules
chmod 0644 /etc/udev/rules.d/99-uinput.rules

# ---------------------------------------------------------------------------
# 19. Install the kiosk session script
# ---------------------------------------------------------------------------
echo "==> Installing kiosk session script to ${SESSION_SCRIPT}..."
install -m 0755 "${SCRIPT_DIR}/start-kiosk-session.sh" "${SESSION_SCRIPT}"

# ---------------------------------------------------------------------------
# 20. Register the X session
# ---------------------------------------------------------------------------
echo "==> Registering charthub-kiosk X session at ${SESSION_DESKTOP}..."
mkdir -p "$(dirname "${SESSION_DESKTOP}")"
cat > "${SESSION_DESKTOP}" <<'SESSION_EOF'
[Desktop Entry]
Name=ChartHub Kiosk
Comment=Minimal kiosk session for ChartHub.Server
Exec=/usr/local/bin/start-charthub-kiosk
Type=Application
SESSION_EOF

# ---------------------------------------------------------------------------
# 21. Configure LightDM autologin into the kiosk session
# ---------------------------------------------------------------------------
AUTOLOGIN_USER="${KIOSK_USER:-${SUDO_USER:-$(logname 2>/dev/null || echo "")}}"
if [[ -z "$AUTOLOGIN_USER" ]]; then
    echo "WARNING: Could not determine the autologin user."
    echo "         Set KIOSK_USER environment variable and re-run, or edit ${LIGHTDM_CONF} manually."
    AUTOLOGIN_USER="<your-user>"
fi

echo "==> Writing LightDM config to ${LIGHTDM_CONF} (autologin user: ${AUTOLOGIN_USER})..."
mkdir -p "$(dirname "${LIGHTDM_CONF}")"
cat > "${LIGHTDM_CONF}" <<LIGHTDM_EOF
[Seat:*]
autologin-user=${AUTOLOGIN_USER}
autologin-user-timeout=0
user-session=charthub-kiosk
greeter-session=lightdm-gtk-greeter

[SeatDefaults]
xserver-command=X -s 0 -dpms
LIGHTDM_EOF

GREETER_CONF="/etc/lightdm/lightdm-gtk-greeter.conf"
if [[ -f "$GREETER_CONF" ]]; then
    echo "==> Disabling screensaver in lightdm-gtk-greeter.conf..."
    if ! grep -q "^\[greeter\]" "$GREETER_CONF"; then
        printf '\n[greeter]\n' >> "$GREETER_CONF"
    fi
    sed -i '/^screensaver-timeout/d' "$GREETER_CONF"
    sed -i '/^\[greeter\]/a screensaver-timeout=0' "$GREETER_CONF"
fi

# ---------------------------------------------------------------------------
# 22. Install ChartHub Plymouth boot splash
#     Runs install-plymouth-theme.sh which handles asset generation,
#     theme registration, GRUB patching, and initramfs rebuild.
# ---------------------------------------------------------------------------
echo "==> Installing ChartHub Plymouth boot splash..."
bash "${SCRIPT_DIR}/install-plymouth-theme.sh"

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "========================================="
echo " Kiosk setup complete."
echo "========================================="
echo " Next steps:"
echo "   1. Verify autologin user in ${LIGHTDM_CONF}: ${AUTOLOGIN_USER}"
echo "   2. After first boot: sudo tailscale up --authkey <key>"
echo "   3. Copy the private key printed below into GitHub Secret SERVER_DEPLOY_SSH_PRIVATE_KEY"
echo "   4. Run CI deploy — it will drop the binary and start the server"
echo ""
echo " Installed runtime packages:"
echo "   xserver-xorg + lightdm + openbox     — X11 display stack"
echo "   libgl1-mesa-dri / mesa-vulkan-drivers — AMD Radeon R5 OpenGL + Vulkan"
echo "   xserver-xorg-video-amdgpu / -radeon   — X11 KMS display drivers for AMD GCN"
echo "   linux-firmware                        — AMD GPU microcode"
echo "   libdrm2 / libdrm-amdgpu1              — kernel DRM interface"
echo "   pulseaudio / alsa-utils               — audio (USB drum, USB mic, games)"
echo "   bluez                                 — Bluetooth game controllers"
echo "   ffmpeg                                — media codecs"
echo "   openssh-server                        — CI SSH access"
echo "   dotnet-runtime-10.0 + aspnetcore-runtime-10.0 — ChartHub.Server target"
echo "   dotnet-runtime-8.0  + aspnetcore-runtime-8.0  — fallback"
echo "   tailscale                             — secure remote access"
echo "========================================="
echo ""
echo "========================================="
echo " ACTION REQUIRED: Save this private key as"
echo " GitHub Secret: SERVER_DEPLOY_SSH_PRIVATE_KEY"
echo "-----------------------------------------"
printf '%s\n' "${DEPLOY_SSH_PRIVATE_KEY}"
echo "-----------------------------------------"
echo " Public key baked into /home/gamer/.ssh/authorized_keys:"
printf '%s\n' "${DEPLOY_SSH_PUBLIC_KEY}"
echo "========================================="
