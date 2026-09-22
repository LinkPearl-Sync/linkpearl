#!/usr/bin/env bash
# Compile le plugin en Debug et le copie vers le dossier de dev plugin Windows,
# pour tester depuis WSL sans passer par une release.
#
# Ne publie rien : pas de tag, pas de push, aucun effet hors de la machine.
#
# Usage : ./scripts/deploy-plugin-dev.sh
#         LINKPEARL_DEV_PLUGIN_DIR=/autre/chemin ./scripts/deploy-plugin-dev.sh
#
# Côté Dalamud, une seule configuration à faire une fois :
#   /xlsettings > Experimental > Dev Plugin Locations > ajouter
#   C:\Users\yann\XIVDev\Linkpearl

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/Linkpearl/Linkpearl.csproj"
BUILD_DIR="$REPO_ROOT/Linkpearl/bin/Debug"
DEST="${LINKPEARL_DEV_PLUGIN_DIR:-/mnt/c/Users/yann/XIVDev/Linkpearl}"

# Le SDK Dalamud cherche ses assemblies dans ~/.xlcore sous Linux, alors que le
# Dalamud à jour est celui de XIVLauncher côté Windows. On les aligne avant de
# compiler, sinon on construit contre une API plus ancienne que celle qui
# chargera le plugin.
WINDOWS_HOOKS="$(ls -d /mnt/c/Users/*/AppData/Roaming/XIVLauncher/addon/Hooks/dev 2>/dev/null | head -1 || true)"
LINUX_HOOKS="$HOME/.xlcore/dalamud/Hooks/dev"

if [ -n "$WINDOWS_HOOKS" ] && [ -d "$WINDOWS_HOOKS" ]; then
  echo "==> Alignement des assemblies Dalamud depuis $WINDOWS_HOOKS"
  mkdir -p "$LINUX_HOOKS"
  rsync -a --delete "$WINDOWS_HOOKS/" "$LINUX_HOOKS/"
fi

echo "==> Compilation Debug"
dotnet build "$PROJECT" -c Debug --nologo

if [ ! -f "$BUILD_DIR/Linkpearl.dll" ]; then
  echo "Erreur : $BUILD_DIR/Linkpearl.dll est introuvable." >&2
  exit 1
fi

echo "==> Copie vers $DEST"
mkdir -p "$DEST"
for f in Linkpearl.dll Linkpearl.json Linkpearl.deps.json Linkpearl.pdb \
         Penumbra.Api.dll Glamourer.Api.dll LiteNetLib.dll; do
  if [ -f "$BUILD_DIR/$f" ]; then
    cp -f "$BUILD_DIR/$f" "$DEST/"
  fi
done

echo "==> Déployé :"
ls -la "$DEST"
echo
echo "Dans le jeu : /xlplugins > Dev Tools > recharger le plugin."
