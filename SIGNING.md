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
