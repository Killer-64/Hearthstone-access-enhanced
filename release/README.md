# Hearthstone Access for Mac (unofficial)

Unofficial macOS port of [Hearthstone Access](https://hearthstoneaccess.com), the mod that makes Hearthstone playable for blind players. Same features and keyboard shortcuts as on Windows; speech goes through the macOS speech synthesizer via [Prism](https://github.com/ethindp/prism).

Not affiliated with Blizzard Entertainment or with the Hearthstone Access developers. Use it at your own risk. Source code: https://github.com/Killer-64/Hearthstone-access-enhanced

## Requirements

- A Mac with Apple silicon (Intel Macs are untested) and a recent macOS
- Hearthstone installed with Battle.net in `/Applications/Hearthstone`
- An internet connection during installation
- Xcode Command Line Tools (the installer asks for them if they are missing)
- .NET 8 SDK (downloaded automatically into `~/.dotnet`)

## Install

1. Quit Hearthstone.
2. Open **Install Hearthstone access.command** in this folder (double-click, or run `bash "path/to/Install Hearthstone access.command"` in Terminal). Keep the `Resources` folder next to it.
3. When it says DONE, start the game from Battle.net as usual. The mod turns on by itself.

The installer downloads the official Hearthstone Access release and builds the mod for the game installed on your Mac. After a game update the mod rebuilds itself while the game is closed.

## Uninstall

Open **Uninstall Hearthstone access.command**. It removes the watcher and the mod files and restarts Battle.net without the mod. The game itself is not touched.

## Speech

- Uses your system voice and its settings from System Settings > Accessibility > Spoken Content: voice, speaking rate and volume, per language.
- If "Detect languages" is turned on there, each message is spoken with the voice you chose for its language; if it is off, the system voice is always used.
- Changes in System Settings are picked up while you play.
- Any key press stops the current speech, like a screen reader.

## Troubleshooting

- Logs are in `~/Library/Logs/HearthstoneAccess` (`speech.log`, `loader.log`, `watch.log`).
- If the game was started before the mod was ready, quit it and press Play again; the watcher restarts Battle.net with the mod a few seconds after Battle.net starts.
- If "permission denied" appears when opening the installer, run it with `bash` in Terminal as shown above.

## Credits

- Hearthstone Access: Guide Dev and the Hearthstone Access community developers.
- Prism by Ethin Probst, MPL-2.0 (`Resources/voiceover/prism/LICENSE-prism-MPL-2.0.txt`).
- Hearthstone is a trademark of Blizzard Entertainment.
