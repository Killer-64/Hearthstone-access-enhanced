#!/bin/bash
# Deinstalator Hearthstone Access dla macOS: usuwa strażnika, pliki moda obok gry
# i przywraca zwykłego Battle.neta. Gra zostaje nietknięta.
H="$HOME/Library/Application Support/HearthstoneAccess"
echo "== Hearthstone Access dla Maca - odinstalowanie"
if pgrep -x Hearthstone >/dev/null; then echo "Hearthstone jest włączony. Zamknij grę i uruchom ponownie. Enter zamyka okno."; read -r _; exit 1; fi

launchctl bootout "gui/$(id -u)/pl.hsa-mac.watch" 2>/dev/null
rm -f "$HOME/Library/LaunchAgents/pl.hsa-mac.watch.plist"
echo "Strażnik usunięty."

# pliki moda obok gry (nie są częścią gry)
[ -L /Applications/Hearthstone/Accessibility ] && rm -f /Applications/Hearthstone/Accessibility
rm -rf /Applications/Hearthstone/HearthstoneAccess /Applications/Hearthstone/HearthstoneAccess.previous
rm -rf "$H/src" "$H/hsa-watch.sh"
echo "Pliki moda usunięte."

if pgrep -x Battle.net >/dev/null; then
    osascript -e 'quit app "Battle.net"' >/dev/null 2>&1
    for i in $(seq 1 20); do pgrep -x Battle.net >/dev/null || break; sleep 1; done
    pkill -f 'Agent.app/Contents/MacOS/Agent' 2>/dev/null; sleep 2
    open -a /Applications/Battle.net.app
    echo "Battle.net uruchomiony ponownie, już bez moda."
fi
echo
echo "GOTOWE. Hearthstone Access został usunięty. .NET w ~/.dotnet zostaje (możesz go usunąć ręcznie)."
echo "Naciśnij Enter, żeby zamknąć to okno."
read -r _
