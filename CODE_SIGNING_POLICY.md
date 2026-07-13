# Code Signing Policy

mouseclicker.app (the [`stevologic/mouse_clicker`](https://github.com/stevologic/mouse_clicker)
project) signs its released `MouseClicker.exe` to protect its integrity and
authenticity and to identify the publisher to Windows.

## Certificate

Release binaries are signed with a certificate provided **free of charge by the
[SignPath Foundation](https://signpath.org/)**, using code signing by
[SignPath.io](https://signpath.io/). We gratefully acknowledge their support of
open-source software.

## Team roles

This project is maintained by **[stevologic](https://github.com/stevologic)**,
who fills all SignPath roles:

- **Author** — proposes and commits the changes to be released.
- **Reviewer** — reviews the source and the built artifact before signing.
- **Approver** — manually approves each individual signing request in SignPath.

All maintainers use **multi-factor authentication** for both their GitHub and
SignPath accounts.

## Build & signing process

- Releases are built from public source in **GitHub Actions on `windows-latest`**
  via [`build.ps1`](build.ps1) — no local or unreproducible steps.
- The unsigned artifact is submitted to SignPath, which signs it only after
  **manual approval of each release**. See
  [`.github/workflows/sign-release.yml`](.github/workflows/sign-release.yml).
- The signed `MouseClicker.exe` carries product-name and version metadata that
  match the project (`AssemblyInfo.cs`).

## Privacy policy

mouseclicker.app collects **no personal data**. It runs entirely on your machine
and makes **no network connections except those you explicitly initiate** — for
example, sending a prompt to an AI provider using *your own* API key, or talking
to a local Ollama server you run. Settings are stored locally in
`%APPDATA%\MouseClicker`. There is **no telemetry, no analytics, and no
auto-update**. The program **transfers no information to us or any third party
without your consent.**

## Uninstalling

mouseclicker.app is a single portable executable — nothing is installed
system-wide, no registry keys, no services. To remove it completely:

1. Delete `MouseClicker.exe`.
2. *(Optional)* Delete the settings folder `%APPDATA%\MouseClicker`.
