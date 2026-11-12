#!/usr/bin/env bash
# Install the published build under ~/.local (no root needed).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
[ -x "$here/publish/gidubba" ] || { echo "Run ./build.sh first"; exit 1; }
dest="$HOME/.local/opt/gidubba"
mkdir -p "$dest" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
rm -rf "$dest"/*; cp -r "$here/publish/." "$dest/"
ln -sf "$dest/gidubba" "$HOME/.local/bin/gidubba"
cp "$here/../Assets/gidubba-256.png" "$HOME/.local/share/icons/hicolor/256x256/apps/gidubba.png"
sed "s|@EXEC@|$dest/gidubba|" "$here/gidubba.desktop.in" > "$HOME/.local/share/applications/gidubba.desktop"
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
echo "Installed. Launch 'Gidubba' from your app menu or run: gidubba"
