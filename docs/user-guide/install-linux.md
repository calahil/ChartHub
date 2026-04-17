# Install on Linux

## Requirements

- Ubuntu 22.04+ or equivalent (x64)
- .NET 10 Runtime (if not using the self-contained build)

---

## Download

1. Go to the [Releases](https://github.com/calahilstudios/charthub/releases/latest) page.
2. Download `ChartHub-linux-x64.tar.gz`.
3. Extract:

```bash
tar -xzf ChartHub-linux-x64.tar.gz
cd ChartHub
```

---

## First Run

```bash
./ChartHub
```

On first launch ChartHub creates its config directory at `~/.config/ChartHub`.

---

## Optional: Desktop Entry

To make ChartHub appear in your application launcher, create a `.desktop` file:

```bash
cat > ~/.local/share/applications/charthub.desktop << EOF
[Desktop Entry]
Name=ChartHub
Exec="/path/to/ChartHub/ChartHub"
Icon=/path/to/ChartHub/Resources/charthub.png
Type=Application
Categories=Game;
EOF
```

Replace `/path/to/ChartHub` with the actual extraction path.

---

## Updating

Download the latest release tarball and extract it over the existing directory. Configuration in `~/.config/ChartHub` is preserved.
