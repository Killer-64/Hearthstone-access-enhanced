#!/bin/bash
# Instalator Hearthstone Access dla macOS (nieoficjalny port, mowa przez VoiceOver).
# Buduje moda na tym Macu z Twojej gry i oficjalnej paczki HSA, instaluje
# strażnika, który przy każdym starcie Battle.neta wstrzykuje moda, i restartuje
# Battle.net. Żaden plik gry nie jest zmieniany.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
H="$HOME/Library/Application Support/HearthstoneAccess"
SRC="$H/src"
AGENT="$HOME/Library/LaunchAgents/pl.hsa-mac.watch.plist"
LOADER=/Applications/Hearthstone/HearthstoneAccess/libhsaloader.dylib
say() { echo; echo "== $*"; }
fail() { echo; echo "BŁĄD: $*"; echo "Instalacja przerwana. Naciśnij Enter, żeby zamknąć."; read -r _; exit 1; }

say "Hearthstone Access dla Maca - instalacja"
[ -d /Applications/Hearthstone/Hearthstone.app ] || fail "Nie znaleziono gry w /Applications/Hearthstone. Zainstaluj Hearthstone przez Battle.net."
[ -d /Applications/Battle.net.app ] || fail "Nie znaleziono Battle.net w folderze Aplikacje."
pgrep -x Hearthstone >/dev/null && fail "Hearthstone jest włączony. Zamknij grę i uruchom instalator ponownie."

say "Sprawdzam narzędzia"
xcode-select -p >/dev/null 2>&1 || { xcode-select --install >/dev/null 2>&1; fail "Brak narzędzi Xcode (Command Line Tools). Otworzyło się okno ich instalacji; po zakończeniu uruchom instalator ponownie."; }
command -v python3 >/dev/null || fail "Brak python3 (instaluje się razem z Command Line Tools)."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if ! dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
    say "Instaluję .NET 8 SDK od Microsoftu do ~/.dotnet (około 200 MB, chwilę to potrwa)"
    curl -sSL -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh || fail "Nie udało się pobrać instalatora .NET."
    bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet" >/dev/null || fail "Instalacja .NET się nie powiodła."
fi

say "Kopiuję pliki moda"
mkdir -p "$SRC/downloads"
FILES=""
for d in Resources zrodla; do [ -f "$HERE/$d/rebuild.sh" ] && { FILES="$HERE/$d"; break; }; done
[ -n "$FILES" ] || fail "Nie znaleziono folderu z plikami moda (Resources) obok instalatora."
rsync -a --delete --exclude downloads --exclude work "$FILES/" "$SRC/" || fail "Nie udało się skopiować plików."
chmod +x "$SRC/rebuild.sh" "$SRC/hsa-watch.sh"

say "Pobieram Hearthstone Access z hearthstoneaccess.com i listę zmian moda z GitHuba"
curl -sSL -o "$SRC/downloads/hsa.zip.new" https://hearthstoneaccess.com/files/pre_patch.zip || fail "Nie udało się pobrać paczki Hearthstone Access."
curl -sSL -o "$SRC/downloads/hsa.diff.patch.new" https://raw.githubusercontent.com/antonshusharin/DevTools/master/diff.patch || fail "Nie udało się pobrać diff.patch."
zipver=$(unzip -p "$SRC/downloads/hsa.zip.new" patch/Accessibility/hsa_manifest.json | python3 -c 'import json,sys;print(json.load(sys.stdin)["accessibility_version"])')
gitver=$(curl -sSL https://raw.githubusercontent.com/antonshusharin/DevTools/master/hsa_version | tr -d '[:space:]')
echo "Wersja moda w paczce: $zipver, w repozytorium: $gitver"
[ "$zipver" = "$gitver" ] || fail "Wersja paczki moda ($zipver) nie zgadza się z repozytorium ($gitver). Spróbuj za jakiś czas, gdy autorzy HSA opublikują obie."
mv "$SRC/downloads/hsa.zip.new" "$SRC/downloads/hsa.zip"
mv "$SRC/downloads/hsa.diff.patch.new" "$SRC/downloads/hsa.diff.patch"

say "Buduję moda pod Twoją wersję gry (to potrwa około minuty)"
"$SRC/rebuild.sh" "$SRC/downloads/hsa.zip" || fail "Budowanie moda się nie powiodło. Szczegóły są powyżej."

say "Instaluję strażnika, który włącza moda przy każdym starcie Battle.neta"
mkdir -p "$HOME/Library/LaunchAgents" "$HOME/Library/Logs/HearthstoneAccess"
cp "$SRC/hsa-watch.sh" "$H/hsa-watch.sh"; chmod +x "$H/hsa-watch.sh"
cat > "$AGENT" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>pl.hsa-mac.watch</string>
    <key>ProgramArguments</key><array><string>/bin/bash</string><string>$H/hsa-watch.sh</string></array>
    <key>RunAtLoad</key><true/>
    <key>KeepAlive</key><true/>
    <key>EnvironmentVariables</key><dict><key>PATH</key><string>/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin</string></dict>
</dict>
</plist>
EOF
launchctl bootout "gui/$(id -u)/pl.hsa-mac.watch" 2>/dev/null
launchctl bootstrap "gui/$(id -u)" "$AGENT" || fail "Nie udało się uruchomić strażnika."

say "Uruchamiam Battle.net z modem"
if pgrep -x Battle.net >/dev/null; then
    osascript -e 'quit app "Battle.net"' >/dev/null 2>&1
    for i in $(seq 1 20); do pgrep -x Battle.net >/dev/null || break; sleep 1; done
    pkill -f 'Agent.app/Contents/MacOS/Agent' 2>/dev/null; sleep 2
fi
(cd / && nohup /usr/bin/env DYLD_INSERT_LIBRARIES="$LOADER" /Applications/Battle.net.app/Contents/MacOS/Battle.net >/dev/null 2>&1 &)
sleep 15
for a in $(pgrep -f 'Agent.app/Contents/MacOS/Agent'); do
    ps eww -p "$a" -o command= | grep -q "DYLD_INSERT_LIBRARIES=$LOADER" || { kill "$a" 2>/dev/null; sleep 3; kill -9 "$a" 2>/dev/null; }
done

echo
echo "GOTOWE. Hearthstone Access jest zainstalowany na stałe."
echo "Uruchamiaj grę normalnie z Battle.neta (Graj). Mod włącza się sam, mowa idzie przez VoiceOver."
echo "Po aktualizacji gry mod przebuduje się sam, gdy gra będzie zamknięta."
echo "Żeby usunąć moda, użyj pliku Odinstaluj HSA.command."
echo "Naciśnij Enter, żeby zamknąć to okno."
read -r _
