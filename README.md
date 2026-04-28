# WAshed

**Blur your WhatsApp chats automatically when you're sharing your screen.**

Stop accidentally exposing private messages in screen-shared meetings. WAshed
detects when you're sharing your screen and applies a real GPU blur to
WhatsApp chat regions — automatically, no hotkey required.

> **v0** is the overlay version: blur is visible to both you and viewers
> during screen sharing (same tradeoff as browser blur extensions).
> **v1** ships an Indirect Display Driver so blur appears only in the
> shared stream while your real monitor stays untouched.

---

## Status

🚧 **In development.** Not yet released. Watch this repo for the v0.1.0 release.

---

## Features (v0)

- Automatic activation when screen sharing is detected (Zoom, Teams, Meet, Discord, OBS, more)
- Real GPU blur via Win2D, not opaque overlay rectangles
- Auto-detects chat list and conversation regions in WhatsApp Desktop
- Tray app, no window clutter
- Toggle between blurring chat list, open conversation, or both
- Manual hotkey toggle as fallback

## Roadmap

- [ ] **v0.1.0** — Overlay-based blur for WhatsApp Desktop
- [ ] **v0.2.0** — Refined region detection, more screen-share clients detected
- [ ] **v1.0.0** — Indirect Display Driver: blur only in the shared stream

A separate Chrome extension for WhatsApp Web is planned post-v0 in its own repo.

## Requirements

- Windows 10 version 1903+ or Windows 11
- DirectX 11 capable GPU (any GPU from the last ~10 years)
- WhatsApp Desktop installed (Microsoft Store or web download)

## Installation

Download the latest installer from
[Releases](https://github.com/mohamedasem318/WAshed/releases).

## Building from source

See [docs/building.md](docs/building.md).

## License

WAshed is licensed under the [GNU Affero General Public License v3.0](LICENSE).

This means you are free to use, modify, and distribute this software, but
any modified version — including those run as a network service — must be
released under the same license with source code available to its users.

## Citation

If you use WAshed in your work or build on top of it, an acknowledgment is
appreciated but not legally required beyond what AGPL-3.0 mandates:

> Assem, M. (2026). *WAshed: Automatic chat blur for screen sharing.*
> https://github.com/mohamedasem318/WAshed

## Disclaimer

WAshed is an independent open-source project and is **not affiliated with,
endorsed by, sponsored by, or connected to WhatsApp LLC, Meta Platforms, Inc.,
or any of their subsidiaries**. "WhatsApp" is a trademark of WhatsApp LLC.
WAshed does not modify, repackage, or interfere with the WhatsApp application
itself; it operates as a separate privacy utility that processes the visual
output of windows on the user's own desktop. References to "WhatsApp" in this
project's code, documentation, and user interface exist solely to identify the
target application for the user's privacy convenience.

The "WA" in the project name "WAshed" is used in a descriptive sense to
indicate compatibility with WhatsApp Desktop and does not imply official
endorsement. Users of this software bear sole responsibility for compliance
with WhatsApp's Terms of Service in their own usage.

This software is provided "as is", without warranty of any kind. The author
assumes no liability for any consequences arising from the use of this
software, including but not limited to: failure to blur sensitive content,
incompatibility with future versions of WhatsApp Desktop, or any privacy
incident occurring during screen sharing.

## Contact

Mohamed Assem — [Portfolio](https://mohamedasem318.github.io/portfolio) · [LinkedIn](https://linkedin.com/in/mohamedasem318)