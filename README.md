# Xbox Achievement Unlocker

> [!NOTE]
> This repository is a community-maintained fork and continuation of [Xbox-Achievement-Unlocker](https://github.com/Fumo-Unlockers/Xbox-Achievement-Unlocker) originally created by [Draff / ItsLogic](https://github.com/ItsLogic) and contributors. All credit for the original reverse engineering, research, and architecture belongs to the upstream authors.

Unlock achievements on Microsoft/Xbox games with ease. This tool is inspired by the functionality of Steam Achievements Manager and is completely free to use.

## Table of Contents

- [Xbox Achievement Unlocker](#xbox-achievement-unlocker)
  - [Table of Contents](#table-of-contents)
  - [About Xbox Achievement Unlocker](#about-xbox-achievement-unlocker)
  - [How It Works](#how-it-works)
  - [Requirements](#requirements)
  - [Features](#features)
  - [Screenshots](#screenshots)
  - [Events Guide](#events-guide)
  - [Usage Guide](#usage-guide)
  - [Future Improvements](#future-improvements)
  - [Acknowledgements & Upstream](#acknowledgements--upstream)
  - [License](#license)

## About Xbox Achievement Unlocker

There are numerous paid services offering tools or services to unlock a full game's achievements on your account. Xbox Achievement Unlocker is a free alternative that doesn't randomly add gamerscore from arbitrary games or charge you to unlock a game's achievements.

## How It Works

Xbox Achievement Unlocker uses code from memory.dll to extract the user's XAuth token from one of the Xbox app processes. This token is then used to make web requests to Xbox servers, pulling information on achievements and informing the server which of these achievements have been unlocked.

## Requirements

- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Xbox App for Windows](https://apps.microsoft.com/store/detail/xbox/9MV0B5HZVK9Z)

## Features

- Extract XAuth from Xbox app or use OAuth to login
- Obtain a list of games from the user or any selected XUID
- Unlock Achievements for any Title Managed game
- Unlock Event-based achievements with cached telemetry tokens
- Spoof time and presence in any game
- Built-in HTTP server for local API automation

## Screenshots

Coming soon.

## Events Guide

See [Events Guide](./Doc/Events.md) for details on event-based achievement mechanics.

## Acknowledgements & Upstream

This project builds on the foundational work created by:
- **[ItsLogic / Draff](https://github.com/ItsLogic)** — Original creator of Xbox-Achievement-Unlocker
- **[Fumo-Unlockers](https://github.com/Fumo-Unlockers)** — Community maintenance and event database
- **[XboxAuthNet](https://github.com/XboxAuthNet)** — Xbox authentication library
- **[WPF-UI](https://github.com/lepoco/wpfui)** — Modern Fluent UI components

## License

The UI for this program was built on top of the WPF-UI Fluent template which is MIT licensed. Any and all modifications and/or additions to this template are GNU GPL licensed. You can find a copy of the licenses in [LICENSE][LICENSE] and [LICENSE.MIT][MIT-LICENSE].
This tool uses the XboxAuthNet library which is also MIT licensed.

[LICENSE]: LICENSE
[MIT-LICENSE]: LICENSE.MIT
