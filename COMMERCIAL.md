# Commercial licensing

Gausslite is open-source software, released under the
[GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0). For the vast
majority of uses — running it yourself, using it in your team, building on it
in another open-source project under AGPL — **you don't need anything
beyond the AGPL**. Read it, follow it, you're set.

This document is for the cases where AGPL terms don't fit your situation.

## When you might need a commercial license

You probably want to talk to me if **any** of these apply to your intended
use:

- You want to **embed Gausslite (in part or in whole)** in proprietary
  software you ship to customers, and you do not want to release that
  software under AGPL-3.0.
- You want to **provide Gausslite as a network service** (or a derivative of
  it) to your users, and you do not want to expose the modified source code
  to those users as AGPL-3.0 §13 requires.
- You want to **redistribute a modified or extended version** of Gausslite
  under license terms different from AGPL-3.0.
- You want to **bundle Gausslite into a commercial product** (e.g. an
  installer, a SaaS dashboard, an enterprise-distribution package) where
  the AGPL's source-availability obligations are incompatible with your
  contractual terms.
- You're a **company** whose policies forbid pulling AGPL-licensed code
  into your codebase, even for internal-only use, and you want a clean
  license to operate under.

If your use case isn't on this list and you're not sure, send me an email
and we'll figure it out — it's usually obvious within a few messages
whether you need anything beyond AGPL.

## How to get one

Email **`mohamedasem318@gmail.com`** with:

1. A short description of your intended use (one paragraph is fine).
2. The scope you'd want covered (e.g. "embed in our internal employee-monitoring
   tool", "ship to customers as part of our screen-recording suite", etc.).
3. Whether you're a company, an indie developer, an academic / research group,
   or something else.

Pricing depends on the scope. Indie / single-developer use cases are very
different from "global enterprise deploy across 50,000 seats" — I'll send
back a proposal with terms.

## What you don't need a commercial license for

To be unambiguous: **you do not need a commercial license** for any of
these:

- Running Gausslite on your own machine, however you want, for any reason.
- Sharing it with friends, posting tutorials, embedding screenshots in blog
  posts, recommending it on social media, etc.
- Forking the repo on GitHub, modifying it for your own use, and running
  the modified version yourself.
- Contributing improvements back via pull request (those land under the
  same AGPL license, see [CONTRIBUTING.md](CONTRIBUTING.md)).
- Using Gausslite as a tool in your own workflow, even at work.
- Open-source projects building on Gausslite that are themselves
  AGPL-3.0-compatible.

The AGPL is permissive about *use*. It only kicks in when you want to
distribute or expose Gausslite-derived software to others without the
copyleft obligations.

## Why dual-licensing?

Gausslite is a passion project. AGPL keeps it permanently open and prevents
"embrace-extend-extinguish" scenarios where a company forks it, builds a
proprietary product on top, and never gives the improvements back. The
commercial license is the mechanism that lets companies who *can't* operate
under AGPL still use the software — and lets that revenue support
continued development of the open-source version.

Same model as GitLab, MongoDB (pre-2018), Mattermost, Sentry, Plausible,
and many others.

## Trademark

"Gausslite" is the project name and may not be used to endorse or promote
products derived from this software without the author's written permission.
The AGPL covers the code; trademark rights are separate.

---

For all other questions about Gausslite, the GitHub repository is the
primary contact channel:
[issues](https://github.com/mohamedasem318/Gausslite/issues),
[discussions](https://github.com/mohamedasem318/Gausslite/discussions).
