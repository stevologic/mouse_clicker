# Code signing — removing the Windows "unknown publisher" warning

Windows shows the SmartScreen / "unknown publisher" prompt for any executable
that isn't signed with a certificate it trusts. This project is set up to sign
releases through **[SignPath Foundation](https://signpath.io/open-source)**,
which provides **free** code-signing certificates to eligible open-source
projects. SignPath signs in their cloud — the private key never touches this
repo or your machine.

> **What signing does:** it replaces "Unknown publisher" with a real publisher
> name. With SignPath (an OV-class certificate) the SmartScreen "Windows
> protected your PC" reputation prompt then fades as downloads accumulate;
> it is not instant like an EV certificate, but it is free and it removes the
> scary "unknown publisher" text.

## This repo already qualifies

- ✅ Public GitHub repository
- ✅ OSI-approved license (MIT)
- ✅ Not a fork
- ✅ Reproducible CI build (`build.ps1` runs on `windows-latest`)

> ⚠️ Heads-up: auto-clickers are sometimes flagged by AV heuristics as "PUA".
> SignPath reviews applications manually and may ask about this. Be ready to
> explain that it's a general-purpose, source-available desktop tool.

## Steps

1. **Apply** at <https://signpath.io/open-source> with this repository. Wait for
   approval (SignPath sets up an *organization*, a *project*, and a *signing
   policy* for you).
2. **Create an API token** in SignPath: *User settings → API tokens*.
3. **Add the repo secrets/variables** (GitHub → repo **Settings → Secrets and
   variables → Actions**):

   | Kind | Name | Value |
   | --- | --- | --- |
   | Secret | `SIGNPATH_API_TOKEN` | the SignPath API token |
   | Variable | `SIGNPATH_ORGANIZATION_ID` | your SignPath organization GUID |
   | Variable | `SIGNPATH_PROJECT_SLUG` | usually `mouse_clicker` |
   | Variable | `SIGNPATH_POLICY_SLUG` | e.g. `release-signing` |

4. That's it. The workflow [`.github/workflows/sign-release.yml`](.github/workflows/sign-release.yml)
   is inert (build-only) until those are set. Once they are, **publishing a
   GitHub release** builds `MouseClicker.exe`, sends it to SignPath, and
   attaches the **signed** exe back to that release automatically.

> SignPath's project **"CI integration"** page generates the exact
> `signpath/github-action-submit-signing-request` step (with your org id and
> the current action version). If it differs from the scaffolded step, paste
> theirs — the surrounding build/upload/attach steps stay the same.

### The workflow file

Save this as `.github/workflows/sign-release.yml`. (Adding a workflow file needs
a GitHub token with the `workflow` scope, which is why it may not have been
pushed automatically — commit it yourself, or create it via the GitHub web UI
where your normal login has the scope.)

```yaml
name: Sign release build (SignPath)

on:
  release:
    types: [published]
  workflow_dispatch:

permissions:
  contents: write   # upload the signed asset to the release

jobs:
  build-sign-attach:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Build MouseClicker.exe
        run: powershell -ExecutionPolicy Bypass -File build.ps1

      - name: Upload unsigned build (input for SignPath)
        id: unsigned
        uses: actions/upload-artifact@v4
        with:
          name: MouseClicker-unsigned
          path: MouseClicker.exe
          if-no-files-found: error

      - name: Sign with SignPath
        id: sign
        if: ${{ vars.SIGNPATH_ORGANIZATION_ID != '' }}
        uses: signpath/github-action-submit-signing-request@v1
        with:
          api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
          organization-id: ${{ vars.SIGNPATH_ORGANIZATION_ID }}
          project-slug: ${{ vars.SIGNPATH_PROJECT_SLUG }}
          signing-policy-slug: ${{ vars.SIGNPATH_POLICY_SLUG }}
          github-artifact-id: ${{ steps.unsigned.outputs.artifact-id }}
          wait-for-completion: true
          output-artifact-directory: signed

      - name: Attach signed exe to the release
        if: ${{ vars.SIGNPATH_ORGANIZATION_ID != '' && github.event_name == 'release' }}
        run: gh release upload "${{ github.event.release.tag_name }}" "signed/MouseClicker.exe" --clobber
        env:
          GH_TOKEN: ${{ github.token }}
```

## Signing a build locally (any cert)

`build.ps1` can also Authenticode-sign locally with any code-signing
certificate — no Windows SDK / signtool required (it uses PowerShell's built-in
`Set-AuthenticodeSignature`, SHA-256 + RFC3161 timestamp):

```powershell
# a .pfx file …
$env:CODESIGN_PFX = "C:\path\to\cert.pfx"; $env:CODESIGN_PFX_PASSWORD = "…"
powershell -ExecutionPolicy Bypass -File build.ps1 -Sign

# … or a cert already in your Windows store
$env:CODESIGN_THUMBPRINT = "ABCD…1234"
powershell -ExecutionPolicy Bypass -File build.ps1 -Sign
```

A **self-signed** certificate is only good for testing the pipeline — Windows
doesn't trust it, so end users would still see the warning.

## Ready-to-submit SignPath Foundation application

Copy these into the application form at <https://signpath.io/open-source>.

- **Project name:** mouseclicker.app
- **Repository:** https://github.com/stevologic/mouse_clicker
- **Website:** https://mouseclicker.app
- **License:** MIT (OSI-approved)
- **Language / build:** C# / .NET Framework WinForms, built in GitHub Actions on
  `windows-latest` via `build.ps1`; single portable ~130 KB `MouseClicker.exe`.
- **Code signing policy:** https://github.com/stevologic/mouse_clicker/blob/main/CODE_SIGNING_POLICY.md
- **MFA:** enabled on GitHub (and will be on SignPath) — required by the program.

**Project description:**
> mouseclicker.app is a free, open-source, no-install Windows auto clicker. It
> automates mouse clicks and cursor movement with configurable buttons, timing,
> and on-screen targeting, can record and replay a click sequence, and can turn
> a plain-English description into a click pattern via the user's own AI key or
> a local model. It ships as a single self-contained executable that runs on the
> .NET Framework already in Windows — no installer, no bundled runtime.

**Why signing is needed:**
> The release is a downloadable `.exe`, so unsigned it triggers the SmartScreen
> "unknown publisher" prompt, which scares off legitimate users of a free
> hobby project. Signing lets Windows show the real publisher.

**Proactively addressing the "auto-clicker / PUA" question** (SignPath reviews
manually and may ask):
> It is a general-purpose desktop automation tool, not malware or a cheat: the
> full source is public and auditable, it runs as the invoking user with no
> elevation, it only performs the input the user explicitly configures, and it
> does not hide, persist, self-update, exfiltrate data, or target any specific
> application. Some AV engines heuristically label *any* input-synthesizing tool
> "PUA"; a scan by Microsoft Defender comes back clean. The README carries a
> "Responsible use" notice that automation may be disallowed by some games and
> services.

**Proactively addressing keyboard recording** (the macro recorder can optionally
capture keystrokes, which review may flag as keylogger-like):
> The Record feature can capture keystrokes *only* when the user ticks an
> off-by-default "Also record keystrokes" checkbox, so a recorded macro can
> reproduce them. This is standard macro-recorder behavior (as in AutoHotkey,
> Pulover's Macro Creator, etc.). Nothing is captured unless the user starts a
> recording with that option on; the input stays entirely on the user's machine,
> is only written to a local recording file the user chooses to save, and is
> never transmitted anywhere. This is documented in the code-signing policy and
> in-app.

Once approved, follow the setup steps above.
