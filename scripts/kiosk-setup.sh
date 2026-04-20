#!/usr/bin/env bash
# kiosk-setup.sh
# Configures a minimal Xubuntu 24.04.3 machine to run ChartHub.Server as a kiosk.
#
# Designed to be run inside Cubic's chroot before ISO export, producing an image
# where a fresh install requires only CI deployment to become fully operational.
#
# What this script does:
#   - Creates 'gamer' user and /srv/appdata/charthub/prod/ directory tree
#   - Installs: Openbox, dotnet-runtime-8.0+10.0, aspnetcore-runtime-8.0+10.0, Tailscale
#   - Removes: XFCE4, CUPS, build toolchain (GCC/Clang/Go/Node/Rust/C-dev-headers),
#     .NET SDK, crash reporters, cloud-init, snapd, spell checkers, avahi, NFS
#   - Protects AMD Radeon integrated GPU stack from auto-removal
#   - Disables LightDM screen lock; configures charthub-kiosk autologin session
#   - Installs and enables charthub-server.service (binary provided by CI on first deploy)
#
# ---------------------------------------------------------------------------
# Reference hardware (emubox — production kiosk):
#
#   OS:     Xubuntu 24.04.3 LTS (Noble Numbat) — minimal install
#   CPU:    AMD A9-9400 Radeon R5, 5 Compute Cores 2C+3G @ 2.40 GHz (2 threads)
#   GPU:    AMD Radeon R5 Graphics (Bristol Ridge, GCN 1.2 integrated APU)
#           — no discrete GPU; Mesa radeonsi is the only OpenGL/Vulkan stack
#   RAM:    8 GB DDR4
#   Audio:  USB microphone + USB e-drum kit (MIDI) + Xbox 360 wireless receiver
#   Games:  YARG, Clone Hero (Unity/SDL2, require X11 WM + Mesa + PulseAudio)
#
# If your hardware differs (e.g. NVIDIA GPU, different CPU) review:
#   - Section 2: GPU/Mesa packages to protect and reinstall
#   - Section 18: dkms/kernel-headers removal safety check
#   - Section 7: Bluetooth — remove bluez entirely if no BT controllers
# ---------------------------------------------------------------------------
#
# Based on package inventory from merges/install.txt (Xubuntu 24.04.3 on emubox).
# Packages confirmed present before removing; uses --auto-remove throughout.

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
# 1a. Refresh package lists (required inside Cubic chroot and on fresh installs)
# ---------------------------------------------------------------------------
echo "==> Refreshing package lists..."
apt-get update -q

# ---------------------------------------------------------------------------
# 2. Protect GPU, display, and audio packages from auto-removal
#    Must run BEFORE any apt-get remove --auto-remove step.
#
#    AMD Radeon R5 (GCN integrated, APU): Mesa DRI/GLX/Vulkan are the only
#    OpenGL/Vulkan stack — removing them kills X11 and Unity/SDL2 rendering.
#    xserver-xorg-video-amdgpu / -radeon: X11 KMS display drivers.
#    linux-firmware: AMD GPU microcode on Ubuntu (firmware-amd-graphics is
#                    Debian-only; Ubuntu ships all firmware in linux-firmware).
#    libdrm-amdgpu1 / libdrm2: kernel DRM interface used by Mesa and X11.
#    libvulkan1 / mesa-vulkan-drivers: Vulkan, used by YARG and CloneHero.
#    libgles2: OpenGL ES (libgles2-mesa was merged into libgles2 on Ubuntu 24.04).
# ---------------------------------------------------------------------------
echo "==> Protecting GPU, display, and audio packages from auto-removal..."
apt-mark manual \
    libgl1-mesa-dri \
    libglx-mesa0 \
    libgles2 \
    mesa-vulkan-drivers \
    libvulkan1 \
    libdrm2 \
    libdrm-amdgpu1 \
    libdrm-radeon1 \
    xserver-xorg-video-amdgpu \
    xserver-xorg-video-radeon \
    xserver-xorg-core \
    xserver-xorg \
    linux-firmware \
    pulseaudio \
    pulseaudio-utils \
    libasound2 \
    alsa-utils \
    || true

# Also ensure Mesa and firmware are installed if they were already removed
echo "==> Ensuring AMD GPU stack is installed..."
apt-get install -y \
    libgl1-mesa-dri \
    libglx-mesa0 \
    libgles2 \
    mesa-vulkan-drivers \
    libvulkan1 \
    libdrm2 \
    libdrm-amdgpu1 \
    xserver-xorg-video-amdgpu \
    xserver-xorg-core \
    linux-firmware \
    || true

# ---------------------------------------------------------------------------
# 3. Install Openbox
# ---------------------------------------------------------------------------
echo "==> Installing Openbox..."
apt-get install -y openbox

# ---------------------------------------------------------------------------
# 4. Remove XFCE4 desktop environment
# ---------------------------------------------------------------------------
echo "==> Removing XFCE4..."
apt-get remove -y --auto-remove \
    xfce4 \
    xfce4-session \
    xfce4-panel \
    xfce4-terminal \
    xfce4-appfinder \
    xfce4-notifyd \
    xfce4-power-manager \
    xfce4-power-manager-plugins \
    xfce4-screensaver \
    xfce4-screenshooter \
    xfce4-settings \
    xfce4-whiskermenu-plugin \
    xfce4-pulseaudio-plugin \
    xfce4-indicator-plugin \
    xfce4-helpers \
    xfwm4 \
    thunar \
    thunar-volman \
    exo-utils \
    || true

# ---------------------------------------------------------------------------
# 5. Remove print stack (no printers on a gaming kiosk)
# ---------------------------------------------------------------------------
echo "==> Removing CUPS / printer stack..."
apt-get remove -y --auto-remove \
    cups \
    cups-daemon \
    cups-browsed \
    cups-bsd \
    cups-client \
    cups-filters \
    cups-filters-core-drivers \
    cups-ipp-utils \
    cups-ppdc \
    cups-server-common \
    foomatic-db-compressed-ppds \
    openprinting-ppds \
    ghostscript \
    printer-driver-brlaser \
    printer-driver-c2esp \
    printer-driver-foo2zjs \
    printer-driver-foo2zjs-common \
    printer-driver-hpcups \
    printer-driver-m2300w \
    printer-driver-min12xxw \
    printer-driver-pnm2ppa \
    printer-driver-postscript-hp \
    printer-driver-ptouch \
    printer-driver-pxljr \
    printer-driver-sag-gdi \
    printer-driver-splix \
    bluez-cups \
    || true

# ---------------------------------------------------------------------------
# 6. Remove scanner stack (SANE)
# ---------------------------------------------------------------------------
echo "==> Removing SANE scanner stack..."
apt-get remove -y --auto-remove \
    sane-utils \
    sane-airscan \
    libsane1 \
    libsane-hpaio \
    || true

# ---------------------------------------------------------------------------
# 7. Remove Bluetooth UI (bluez itself retained — some game controllers use BT)
#    Remove blueman (GUI), bluez-obexd (file transfer), and BT audio module.
#    If no BT controllers are used at all, also remove bluez here.
#
#    pulseaudio is explicitly marked auto=no (apt-mark manual) before removal
#    of the XFCE pulseaudio plugin to prevent --auto-remove from pulling out
#    the PulseAudio daemon itself — which Unity/SDL2 games require for audio,
#    and which also drives USB microphone and USB audio device routing.
# ---------------------------------------------------------------------------
echo "==> Removing Bluetooth GUI and unused BT services..."
apt-get remove -y --auto-remove \
    blueman \
    bluez-obexd \
    pulseaudio-module-bluetooth \
    || true

# ---------------------------------------------------------------------------
# 8. Remove NFS / RPC (no network file shares on a kiosk)
# ---------------------------------------------------------------------------
echo "==> Removing NFS and RPC..."
apt-get remove -y --auto-remove \
    nfs-kernel-server \
    nfs-common \
    rpcbind \
    || true

# ---------------------------------------------------------------------------
# 9. Remove snapd (no snap packages used; saves ~100–200 MB)
# ---------------------------------------------------------------------------
echo "==> Removing snapd..."
apt-get remove -y --auto-remove snapd || true
# Prevent snap from being re-installed as a dependency in future apt runs
cat > /etc/apt/preferences.d/no-snap <<'EOF'
Package: snapd
Pin: release a=*
Pin-Priority: -1
EOF

# ---------------------------------------------------------------------------
# 10. Remove build toolchain (not needed at runtime)
#    Keeps: make (used by dkms internally), gcc-13-base (shared lib base)
# ---------------------------------------------------------------------------
echo "==> Removing build toolchain..."
apt-get remove -y --auto-remove \
    build-essential \
    gcc \
    gcc-13 \
    gcc-13-x86-64-linux-gnu \
    gcc-x86-64-linux-gnu \
    g++ \
    g++-13 \
    g++-13-x86-64-linux-gnu \
    g++-x86-64-linux-gnu \
    clang \
    clang-18 \
    binutils \
    binutils-x86-64-linux-gnu \
    cpp \
    cpp-13 \
    cpp-13-x86-64-linux-gnu \
    cpp-x86-64-linux-gnu \
    libgcc-13-dev \
    libclang-common-18-dev \
    libclang-cpp18 \
    libclang-rt-18-dev \
    libclang1-18 \
    dpkg-dev \
    pkg-config \
    manpages-dev \
    cargo \
    rustc \
    libstd-rust-1.75 \
    libstd-rust-dev \
    || true

# ---------------------------------------------------------------------------
# 11. Remove .NET SDK; install runtimes needed by ChartHub.Server
#     ChartHub.Server targets net10.0; net8.0 runtime retained for other tools.
# ---------------------------------------------------------------------------
echo "==> Removing .NET SDK (all versions)..."
apt-get remove -y --auto-remove \
    dotnet-sdk-8.0 \
    dotnet-sdk-9.0 \
    dotnet-sdk-10.0 \
    dotnet-templates-8.0 \
    dotnet-templates-9.0 \
    dotnet-templates-10.0 \
    dotnet-apphost-pack-8.0 \
    dotnet-apphost-pack-9.0 \
    dotnet-apphost-pack-10.0 \
    dotnet-targeting-pack-8.0 \
    dotnet-targeting-pack-9.0 \
    dotnet-targeting-pack-10.0 \
    aspnetcore-targeting-pack-8.0 \
    aspnetcore-targeting-pack-9.0 \
    aspnetcore-targeting-pack-10.0 \
    || true

echo "==> Installing .NET runtimes (8.0 + 10.0)..."
# Add Microsoft package feed if not already present
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
    aspnetcore-runtime-10.0 \
    || true

# ---------------------------------------------------------------------------
# 11a. Remove Go toolchain (present in Xubuntu image; not needed at runtime)
# ---------------------------------------------------------------------------
echo "==> Removing Go toolchain..."
apt-get remove -y --auto-remove \
    golang \
    golang-go \
    golang-src \
    golang-doc \
    golang-1.22 \
    golang-1.22-go \
    golang-1.22-src \
    golang-1.22-doc \
    || true

# ---------------------------------------------------------------------------
# 11b. Remove Node.js dev tools (present in Xubuntu image; not needed at runtime)
# ---------------------------------------------------------------------------
echo "==> Removing Node.js dev tools..."
apt-get remove -y --auto-remove \
    nodejs \
    libnode-dev \
    eslint \
    gyp \
    handlebars \
    javascript-common \
    || true
# Remove all libjs-* packages (JS libraries pulled by npm/eslint)
dpkg -l 'libjs-*' 2>/dev/null | awk '/^ii/{print $2}' | xargs -r apt-get remove -y --auto-remove || true

# ---------------------------------------------------------------------------
# 11c. Remove C dev headers (dev-only; not needed at runtime)
# ---------------------------------------------------------------------------
echo "==> Removing C dev headers..."
apt-get remove -y --auto-remove \
    libc6-dev \
    libc-dev-bin \
    libc-devtools \
    libffi-dev \
    libicu-dev \
    icu-devtools \
    libncurses-dev \
    libgcc-13-dev \
    libcrypt-dev \
    libasan8 \
    libobjc-13-dev \
    libitm1 \
    liblsan0 \
    libhwasan0 \
    libtsan2 \
    libubsan1 \
    fakeroot \
    || true

# ---------------------------------------------------------------------------
# 12. Remove crash reporters and telemetry
# ---------------------------------------------------------------------------
echo "==> Removing apport crash reporter..."
apt-get remove -y --auto-remove \
    apport \
    apport-symptoms \
    apport-core-dump-handler \
    python3-apport \
    || true

# ---------------------------------------------------------------------------
# 13. Remove cloud-init (not a cloud VM; saves time on boot)
# ---------------------------------------------------------------------------
echo "==> Removing cloud-init..."
apt-get remove -y --auto-remove cloud-init cloud-guest-utils || true
# Prevent reinstall
echo 'datasource_list: [ None ]' > /etc/cloud/cloud.cfg.d/99-disable.cfg 2>/dev/null || true

# ---------------------------------------------------------------------------
# 14. Remove spell checkers (no text editor on the kiosk)
# ---------------------------------------------------------------------------
echo "==> Removing spell checkers..."
apt-get remove -y --auto-remove \
    aspell \
    aspell-en \
    enchant-2 \
    dictionaries-common \
    || true

# ---------------------------------------------------------------------------
# 15. Remove avahi mDNS (not needed; ChartHub uses direct IP)
#     NOTE: avahi-daemon is pulled by some Bluetooth stack components.
#     Only remove if you have no mDNS discovery need.
# ---------------------------------------------------------------------------
echo "==> Removing avahi-daemon..."
apt-get remove -y --auto-remove avahi-daemon || true

# ---------------------------------------------------------------------------
# 16. Remove bpfcc / bpftrace tracing tools (developer tools, not runtime deps)
# ---------------------------------------------------------------------------
echo "==> Removing bpf tracing tools..."
apt-get remove -y --auto-remove \
    bpfcc-tools \
    bpftrace \
    python3-bpfcc \
    || true

# ---------------------------------------------------------------------------
# 17. Remove synaptic package manager GUI (no GUI package mgmt on kiosk)
# ---------------------------------------------------------------------------
echo "==> Removing synaptic..."
apt-get remove -y --auto-remove synaptic software-properties-gtk || true

# ---------------------------------------------------------------------------
# 18. Remove dkms and linux headers (no kernel modules to recompile)
#     CAUTION: skip this if you have NVIDIA/AMD/WiFi drivers installed via dkms
# ---------------------------------------------------------------------------
echo "==> Checking for dkms modules before removing..."
if dkms status 2>/dev/null | grep -q "^"; then
    echo "    WARNING: dkms has active modules — skipping dkms/linux-headers removal."
    echo "    Active modules:"
    dkms status
else
    echo "    No dkms modules found. Removing dkms and old kernel headers..."
    apt-get remove -y --auto-remove dkms || true
    # Remove stale kernel headers (keep current running kernel's headers if any)
    RUNNING_KERNEL="$(uname -r)"
    dpkg -l 'linux-headers-*' 2>/dev/null | awk '/^ii/{print $2}' | grep -v "$RUNNING_KERNEL" | xargs -r apt-get remove -y --auto-remove || true
fi

# ---------------------------------------------------------------------------
# 19. Install Tailscale
#     Tailscale is pre-installed so CI can SSH to the machine after first boot.
#     After flashing, run manually: sudo tailscale up --authkey <key>
# ---------------------------------------------------------------------------
echo "==> Installing Tailscale..."
curl -fsSL https://pkgs.tailscale.com/stable/ubuntu/noble.nosugar.key \
    | gpg --dearmor -o /usr/share/keyrings/tailscale-archive-keyring.gpg
echo 'deb [signed-by=/usr/share/keyrings/tailscale-archive-keyring.gpg] https://pkgs.tailscale.com/stable/ubuntu noble main' \
    > /etc/apt/sources.list.d/tailscale.list
apt-get update -q
apt-get install -y tailscale
systemctl enable tailscaled

# ---------------------------------------------------------------------------
# 20. Create 'gamer' user (CI deploy user; owns all appdata)
# ---------------------------------------------------------------------------
echo "==> Creating 'gamer' user..."
if ! id gamer &>/dev/null; then
    useradd -m -s /bin/bash -G sudo gamer
    # Lock password — login only via SSH key or autologin
    passwd -l gamer
fi
mkdir -p /home/gamer/.ssh
chown gamer:gamer /home/gamer/.ssh
chmod 700 /home/gamer/.ssh
# Placeholder authorized_keys — CI deploy key must be added before deployment
touch /home/gamer/.ssh/authorized_keys
chown gamer:gamer /home/gamer/.ssh/authorized_keys
chmod 600 /home/gamer/.ssh/authorized_keys

# ---------------------------------------------------------------------------
# 21. Create /srv/appdata/charthub/{prod,staging,dev}/ directory trees
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
# 22. Install and enable charthub-server.service
#     The binary does not exist yet — CI provides it on first deploy.
#     systemd will fail to start until the binary is deployed; that is expected.
# ---------------------------------------------------------------------------
echo "==> Installing charthub-server.service..."
install -m 0644 "${SCRIPT_DIR}/../.github/systemd/charthub-server.service" \
    /etc/systemd/system/charthub-server.service
systemctl daemon-reload
systemctl enable charthub-server.service
echo "    charthub-server.service installed and enabled."
echo "    NOTE: Start will fail until CI deploys the binary."

# ---------------------------------------------------------------------------
# 23. Final autoremove pass (catch any orphaned deps)
# ---------------------------------------------------------------------------
echo "==> Final autoremove..."
apt-get autoremove -y --purge

# ---------------------------------------------------------------------------
# 24. Install the kiosk session script
# ---------------------------------------------------------------------------
echo "==> Installing kiosk session script to ${SESSION_SCRIPT}..."
install -m 0755 "${SCRIPT_DIR}/start-kiosk-session.sh" "${SESSION_SCRIPT}"

# ---------------------------------------------------------------------------
# 25. Register the X session
# ---------------------------------------------------------------------------
echo "==> Registering charthub-kiosk X session at ${SESSION_DESKTOP}..."
mkdir -p "$(dirname "${SESSION_DESKTOP}")"
cat > "${SESSION_DESKTOP}" <<'EOF'
[Desktop Entry]
Name=ChartHub Kiosk
Comment=Minimal kiosk session for ChartHub.Server
Exec=/usr/local/bin/start-charthub-kiosk
Type=Application
EOF

# ---------------------------------------------------------------------------
# 26. LightDM: auto-login + disable greeter screen lock
# ---------------------------------------------------------------------------
AUTOLOGIN_USER="${KIOSK_USER:-${SUDO_USER:-$(logname 2>/dev/null || echo "")}}"
if [[ -z "$AUTOLOGIN_USER" ]]; then
    echo "WARNING: Could not determine the autologin user."
    echo "         Set KIOSK_USER environment variable and re-run, or edit ${LIGHTDM_CONF} manually."
    AUTOLOGIN_USER="<your-user>"
fi

echo "==> Writing LightDM config to ${LIGHTDM_CONF} (autologin user: ${AUTOLOGIN_USER})..."
mkdir -p "$(dirname "${LIGHTDM_CONF}")"
cat > "${LIGHTDM_CONF}" <<EOF
[Seat:*]
autologin-user=${AUTOLOGIN_USER}
autologin-user-timeout=0
user-session=charthub-kiosk
greeter-session=lightdm-gtk-greeter

[SeatDefaults]
xserver-command=X -s 0 -dpms
EOF

# ---------------------------------------------------------------------------
# 27. Disable lightdm-gtk-greeter screen locker (if present)
# ---------------------------------------------------------------------------
GREETER_CONF="/etc/lightdm/lightdm-gtk-greeter.conf"
if [[ -f "$GREETER_CONF" ]]; then
    echo "==> Disabling screensaver in lightdm-gtk-greeter.conf..."
    if ! grep -q "^\[greeter\]" "$GREETER_CONF"; then
        echo "" >> "$GREETER_CONF"
        echo "[greeter]" >> "$GREETER_CONF"
    fi
    sed -i '/^screensaver-timeout/d' "$GREETER_CONF"
    sed -i '/^\[greeter\]/a screensaver-timeout=0' "$GREETER_CONF"
fi

echo ""
echo "========================================="
echo " Kiosk setup complete."
echo "========================================="
echo " Next steps:"
echo "   1. Verify autologin user in ${LIGHTDM_CONF}: ${AUTOLOGIN_USER}"
echo "   2. After first boot: sudo tailscale up --authkey <key>"
echo "   3. Add CI deploy SSH public key to /home/gamer/.ssh/authorized_keys"
echo "   4. Run CI prod deploy — it will drop the binary and start the server"
echo ""
echo " Packages intentionally kept:"
echo "   libgl1-mesa-dri / mesa-vulkan-drivers — AMD Radeon R5 OpenGL + Vulkan"
echo "   xserver-xorg-video-amdgpu             — X11 KMS display driver for AMD GCN"
echo "   linux-firmware                        — AMD GPU microcode (Ubuntu package)"
echo "   libdrm2 / libdrm-amdgpu1              — kernel DRM interface"
echo "   bluez                                 — Bluetooth game controllers"
echo "   ffmpeg                                — game audio/video codecs"
echo "   dotnet-runtime-8.0 + aspnetcore-runtime-8.0   — retained as fallback"
echo "   dotnet-runtime-10.0 + aspnetcore-runtime-10.0 — ChartHub.Server target"
echo "   alsa-utils    — audio + MIDI (USB drum kit, USB mic)"
echo "   pulseaudio    — audio daemon required by Unity/SDL2 games and USB audio"
echo "   openbox       — WM required by Unity/SDL2 games"
echo "   tailscale     — remote SSH access for CI deployment"
echo "=========================================" 
