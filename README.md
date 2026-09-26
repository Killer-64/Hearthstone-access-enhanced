# Hearthstone Access for Mac (unofficial)

Unofficial macOS port of [Hearthstone Access](https://hearthstoneaccess.com), the mod that makes Hearthstone playable for blind players. Same features and keyboard shortcuts as on Windows; speech goes through the macOS speech synthesizer via [Prism](https://github.com/ethindp/prism).

Nieoficjalny port moda Hearthstone Access na macOS. Te same funkcje i skróty co na Windowsie, mowa przez syntezator macOS (Prism). Instrukcja po polsku niżej.

This project is not affiliated with Blizzard Entertainment or with the Hearthstone Access developers. Use it at your own risk.

## How it works

No game file is modified, so Battle.net has nothing to repair:

- The installer downloads the official Hearthstone Access release from hearthstoneaccess.com and its source diff from the [Hearthstone Access DevTools](https://github.com/antonshusharin/DevTools) repository, and builds the mod **on your Mac** against **your** installed game (`Resources/tools/port`, a Mono.Cecil transplant tool).
- A small loader (`Resources/loader`) is injected into Hearthstone through Battle.net and serves the mod's files to the game from `/Applications/Hearthstone/HearthstoneAccess`.
- Speech: `Resources/tolk` replaces Tolk; `Resources/voiceover` speaks through Prism's AVSpeech backend and follows your System Settings (Accessibility > Spoken Content): voice, rate, volume per language and "Detect languages". Any key press interrupts speech.
- A watcher (LaunchAgent) restarts Battle.net with the mod when it runs without it, and rebuilds the mod after a game update.

This repository contains **no Blizzard code and no Hearthstone Access files**; they are downloaded and combined only on the user's machine.

## Requirements

- Mac with Apple silicon (Intel untested), recent macOS
- Hearthstone installed with Battle.net in `/Applications/Hearthstone`
- Internet connection during installation; Xcode Command Line Tools (the installer asks for them), .NET 8 SDK (downloaded automatically to `~/.dotnet`)

## Install

1. Close Hearthstone.
2. Download `release/Hearthstone access for Mac.zip`, unzip it, open **Install Hearthstone access.command** (or run it with `bash` in Terminal).
3. When it says GOTOWE (done), start the game from Battle.net as usual.

Uninstall: **Uninstall Hearthstone access.command**. Logs: `~/Library/Logs/HearthstoneAccess`.

## Instalacja (PL)

1. Zamknij Hearthstone.
2. Pobierz `release/Hearthstone access for Mac.zip`, rozpakuj i otwórz **Install Hearthstone access.command** (dwuklik albo `bash "…/Install Hearthstone access.command"` w Terminalu).
3. Gdy napisze GOTOWE, uruchamiaj grę normalnie z Battle.neta. Mod włącza się sam, po aktualizacji gry przebudowuje się sam.

Deinstalacja: **Uninstall Hearthstone access.command**. Logi: `~/Library/Logs/HearthstoneAccess`.

## Credits

- Hearthstone Access: Guide Dev and the Hearthstone Access community developers.
- Prism by Ethin Probst, MPL-2.0 (`Resources/voiceover/prism/LICENSE-prism-MPL-2.0.txt`).
- Hearthstone is a trademark of Blizzard Entertainment.
