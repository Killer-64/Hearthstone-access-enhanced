# Hearthstone Access for Mac (unofficial)

Unofficial macOS port of [Hearthstone Access](https://hearthstoneaccess.com), the mod that makes Hearthstone playable for blind players. Same features and keyboard shortcuts as on Windows; speech goes through the macOS speech synthesizer via [Prism](https://github.com/ethindp/prism).

This project is not affiliated with Blizzard Entertainment or with the Hearthstone Access developers. Use it at your own risk.

## Requirements

- A Mac with Apple silicon (Intel Macs are untested) and a recent macOS
- Hearthstone installed with Battle.net in `/Applications/Hearthstone`
- An internet connection during installation
- Xcode Command Line Tools (the installer asks for them if they are missing)
- .NET 8 SDK (downloaded automatically into `~/.dotnet`)

## Install

1. Quit Hearthstone.
2. Download [`release/HS access Mac port.zip`](release/), unzip it and open **Install Hearthstone access.command** (double-click, or run `bash "path/to/Install Hearthstone access.command"` in Terminal).
3. When it says DONE, start the game from Battle.net as usual. The mod turns on by itself.

After a game update the mod rebuilds itself while the game is closed.

To remove it, open **Uninstall Hearthstone access.command**. Logs are in `~/Library/Logs/HearthstoneAccess`.

## Speech

- Uses your system voice and its settings from System Settings > Accessibility > Spoken Content: voice, speaking rate and volume, per language.
- If "Detect languages" is turned on there, each message is spoken with the voice you chose for its language; if it is off, the system voice is always used.
- Changes in System Settings are picked up while you play.
- Any key press stops the current speech, like a screen reader.

## How it works

No game file is modified, so Battle.net has nothing to repair.

- The installer downloads the official Hearthstone Access release from hearthstoneaccess.com and its source diff from the [Hearthstone Access DevTools](https://github.com/antonshusharin/DevTools) repository, then builds the mod **on your Mac** against **your** installed game (`Resources/tools/port`, a Mono.Cecil transplant tool driven by `Resources/rebuild.sh`).
- A small loader (`Resources/loader`) is injected into Hearthstone through Battle.net and serves the mod's files to the game from `/Applications/Hearthstone/HearthstoneAccess`.
- `Resources/tolk` replaces the Tolk screen reader library; `Resources/voiceover` speaks through Prism's AVSpeech backend.
- A watcher (a LaunchAgent, `Resources/hsa-watch.sh`) restarts Battle.net with the mod whenever it runs without it, and rebuilds the mod after a game update.

This repository contains **no Blizzard code and no Hearthstone Access files**; they are downloaded and combined only on the user's own Mac.

## Credits

- Hearthstone Access: Guide Dev and the Hearthstone Access community developers.
- Prism by Ethin Probst, MPL-2.0 (`Resources/voiceover/prism/LICENSE-prism-MPL-2.0.txt`).
- Hearthstone is a trademark of Blizzard Entertainment.

## License

Copyright (c) 2026 Killer-64. All rights reserved. You may download and run the unmodified installer for personal use; modifying or redistributing the code or the package is not permitted. See [LICENSE](LICENSE). Prism keeps its own license (MPL-2.0).
