#!/usr/bin/env bash
# Builds VKonvert.exe, packs it into the installer, and writes dist/VKonvert-Setup-<version>.exe
# Needs the .NET SDK (8 or newer); it cross-compiles the .NET Framework 4.8 app on any OS.
#   ./build.sh                 (uses "dotnet" on PATH)
#   DOTNET=/path/to/dotnet ./build.sh
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="${DOTNET:-dotnet}"
VER=2.0
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$DOTNET" build app/VKonvert.csproj -c Release -nologo -v q
B=app/bin/Release/net48
P=dist/VKonvert
rm -rf "$P" && mkdir -p "$P"
cp "$B"/*.exe "$B"/*.exe.config "$B"/*.dll "$P"/
cp LICENSE "$P"/LICENSE.txt
cp app/README.txt "$P"/README.txt
{
  echo "VKonvert includes the following third-party software. Their licenses follow."
  for f in licenses/*.txt; do
    printf '\n\n==================== %s ====================\n\n' "$(basename "$f" .txt)"
    cat "$f"
  done
} > "$P"/THIRD-PARTY-NOTICES.txt

python3 -I - "$P" setup/payload.zip <<'PY'
import sys, zipfile, os
src, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in sorted(os.listdir(src)):
        z.write(os.path.join(src, name), name)
PY

"$DOTNET" build setup/VKonvertSetup.csproj -c Release -nologo -v q
cp setup/bin/Release/net48/VKonvert-Setup.exe "dist/VKonvert-Setup-$VER.exe"

# Portable: the same app in a ZIP. Unzip anywhere (even a USB stick) and run VKonvert.exe; nothing is installed.
python3 -I - dist "VKonvert" "dist/VKonvert-$VER-Portable.zip" <<'PY'
import sys, zipfile, os
root, folder, out = sys.argv[1], sys.argv[2], sys.argv[3]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in sorted(os.listdir(os.path.join(root, folder))):
        z.write(os.path.join(root, folder, name), folder + "/" + name)
PY
ls -la "dist/VKonvert-Setup-$VER.exe" "dist/VKonvert-$VER-Portable.zip"
