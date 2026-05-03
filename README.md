# Gausslite

**Blur sensitive chats automatically when you're sharing your screen.**

Stop accidentally exposing private messages in screen-shared meetings.
Gausslite detects when you're sharing your screen and applies a real GPU
blur to chat regions — automatically, no hotkey required.

v0.x focuses on WhatsApp Desktop as the first target. Other chat clients
may follow post-v1.0 based on user demand.

> **v0** is the overlay version: blur is visible to both you and viewers
> during screen sharing (same tradeoff as browser blur extensions).
> **v1** ships an Indirect Display Driver so blur appears only in the
> shared stream while your real monitor stays untouched.

---

## Status

🚧 **In development.** Not yet publicly released. v0.2.0 has shipped
internally; public release is held until v0.x is feature-complete with
a proper installer. Watch this repo for updates.

## Why "Gausslite"?

Portmanteau of *Gaussian* (the blur) and *gaslighting* (what you're
doing to your viewers when they see WhatsApp on your screen but can't
read a word of it). The blur is real; the deniability is up to you.

## Features

- Automatic activation when screen sharing is detected (Zoom, Teams,
  Meet, Discord, OBS)
- Real GPU blur via Win2D, not opaque overlay rectangles
- Auto-detects chat list and conversation regions in WhatsApp Desktop
- Tray app, no window clutter
- Toggle between blurring chat list, open conversation, or both
- Adjustable blur intensity — keep silhouettes readable for yourself
  while staying unreadable to viewers
- Manual hotkey toggle as fallback

## Roadmap

- [x] **v0.1.0** — Overlay-based whole-window blur for WhatsApp Desktop
- [x] **v0.1.1** — Renamed from internal codename, no functional changes
- [x] **v0.2.0** — Region-aware blur (chat list vs. conversation),
      intensity presets, partial-occlusion handling
- [ ] **v0.3.0** — Auto-activation: detect screen-share clients and
      which monitor/window they're capturing
- [ ] **v0.4.0** — Settings window, blur intensity slider, auto-start
      with Windows, settings persistence
- [ ] **v0.5.0** — Toast notification blur during screen sharing
- [ ] **v1.0.0** — Indirect Display Driver: blur appears only in the
      shared stream; your real monitor stays untouched

See [PLAN.md](PLAN.md) for the full milestone breakdown and
architectural detail.

## Requirements

- Windows 10 version 1903+ or Windows 11
- DirectX 11 capable GPU (any GPU from the last ~15 years)
- WhatsApp Desktop installed (Microsoft Store or web download)

## Installation

Pre-public. No installer is available yet. Public release with a signed
installer is planned once v0.x is feature-complete.

If you're a contributor or early tester with repo access, see
[docs/building.md](docs/building.md) for build instructions.

## Building from source

See [docs/building.md](docs/building.md).

## License

Gausslite is licensed under the [GNU Affero General Public License v3.0](LICENSE).

This means you are free to use, modify, and distribute this software,
but any modified version — including those run as a network service —
must be released under the same license with source code available to
its users.

## Citation

If you use Gausslite in your work or build on top of it, an
acknowledgment is appreciated but not legally required beyond what
AGPL-3.0 mandates:

> Assem, M. (2026). *Gausslite: Automatic chat blur for screen sharing.*
> https://github.com/mohamedasem318/Gausslite

## Disclaimer

Gausslite is an independent open-source project and is **not affiliated
with, endorsed by, sponsored by, or connected to WhatsApp LLC, Meta
Platforms, Inc., or any of their subsidiaries**. "WhatsApp" is a
trademark of WhatsApp LLC. Gausslite does not modify, repackage, or
interfere with the WhatsApp application itself; it operates as a
separate privacy utility that processes the visual output of windows on
the user's own desktop. References to "WhatsApp" in this project's code,
documentation, and user interface exist solely to identify the target
application for the user's privacy convenience.

This software is provided "as is", without warranty of any kind. The
author assumes no liability for any consequences arising from the use of
this software, including but not limited to: failure to blur sensitive
content, incompatibility with future versions of WhatsApp Desktop, or
any privacy incident occurring during screen sharing.

## Contact

Mohamed Assem — [Portfolio](https://mohamedasem318.github.io/portfolio) · [LinkedIn](https://linkedin.com/in/mohamedasem318)