#!/usr/bin/env bash
# Tire Fredoka SemiBold et Nunito Regular en statique depuis les fichiers
# variables de Google Fonts, puis vérifie que chaque caractère des chaînes de
# l'interface existe dans la police principale ou dans Inter, derrière.
#
# Statiques parce que l'atlas ImGui ne choisit pas la graisse d'une police
# variable. Le commit de google/fonts et la version de fonttools sont figés :
# c'est ce qui rend les fichiers embarqués reproductibles.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
out="$root/Linkpearl/Assets/Fonts"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

commit=23e54b51ddffbc7713c583748e3bd86f62b1fa4a
base="https://raw.githubusercontent.com/google/fonts/$commit/ofl"

python3 -m venv "$work/venv"
"$work/venv/bin/pip" install --quiet fonttools==4.66.0

curl -fsSL -o "$work/Nunito.ttf"  "$base/nunito/Nunito%5Bwght%5D.ttf"
curl -fsSL -o "$work/Fredoka.ttf" "$base/fredoka/Fredoka%5Bwdth,wght%5D.ttf"
curl -fsSL -o "$out/Nunito-OFL.txt"  "$base/nunito/OFL.txt"
curl -fsSL -o "$out/Fredoka-OFL.txt" "$base/fredoka/OFL.txt"

"$work/venv/bin/fonttools" varLib.instancer -q "$work/Nunito.ttf"  wght=400          -o "$out/Nunito-Regular.ttf"
"$work/venv/bin/fonttools" varLib.instancer -q "$work/Fredoka.ttf" wght=600 wdth=100 -o "$out/Fredoka-SemiBold.ttf"

"$work/venv/bin/python" - "$root" "$out" <<'EOF'
import glob, re, sys
from fontTools.ttLib import TTFont

root, out = sys.argv[1], sys.argv[2]

# Tout caractère non ASCII des littéraux du plugin, hors zone d'usage privé :
# FontAwesome et les symboles du jeu y vivent, et ont leur propre police.
chars = set()
for path in glob.glob(f"{root}/Linkpearl/**/*.cs", recursive=True):
    if "/obj/" in path or "/bin/" in path:
        continue
    for literal in re.findall(r'"(?:[^"\\]|\\.)*"', open(path, encoding="utf-8").read()):
        chars |= {c for c in literal if ord(c) > 127 and not 0xE000 <= ord(c) <= 0xF8FF}

failed = False
for main, backup in {"Nunito-Regular.ttf": "Inter-Regular.ttf",
                     "Fredoka-SemiBold.ttf": "Inter-SemiBold.ttf"}.items():
    primary = TTFont(f"{out}/{main}").getBestCmap()
    fallback = TTFont(f"{out}/{backup}").getBestCmap()
    borrowed = sorted(c for c in chars if ord(c) not in primary and ord(c) in fallback)
    missing = sorted(c for c in chars if ord(c) not in primary and ord(c) not in fallback)
    print(f"{main} : {len(borrowed)} glyphe(s) pris à {backup} : {''.join(borrowed)}")
    if missing:
        print(f"{main} : introuvable, même dans {backup} : {''.join(missing)}")
        failed = True

sys.exit(1 if failed else 0)
EOF
