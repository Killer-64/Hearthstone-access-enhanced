#!/bin/bash
# Hearthstone Access for macOS uninstaller: removes the watcher and the mod files
# next to the game and restores plain Battle.net. The game itself is untouched.
H="$HOME/Library/Application Support/HearthstoneAccess"
echo "== Hearthstone Access for Mac - uninstall"
if pgrep -x Hearthstone >/dev/null; then echo "Hearthstone is running. Quit the game and run this again. Press Enter to close."; read -r _; exit 1; fi

launchctl bootout "gui/$(id -u)/pl.hsa-mac.watch" 2>/dev/null
rm -f "$HOME/Library/LaunchAgents/pl.hsa-mac.watch.plist"
echo "Watcher removed."

# mod files next to the game (not part of the game)
[ -L /Applications/Hearthstone/Accessibility ] && rm -f /Applications/Hearthstone/Accessibility
rm -rf /Applications/Hearthstone/HearthstoneAccess /Applications/Hearthstone/HearthstoneAccess.previous
rm -rf "$H/src" "$H/hsa-watch.sh"
echo "Mod files removed."

if pgrep -x Battle.net >/dev/null; then
    osascript -e 'quit app "Battle.net"' >/dev/null 2>&1
    for i in $(seq 1 20); do pgrep -x Battle.net >/dev/null || break; sleep 1; done
    pkill -f 'Agent.app/Contents/MacOS/Agent' 2>/dev/null; sleep 2
    open -a /Applications/Battle.net.app
    echo "Battle.net restarted without the mod."
fi
echo
echo "DONE. Hearthstone Access has been removed. .NET in ~/.dotnet stays (you can delete it by hand)."
echo "Press Enter to close this window."
read -r _
