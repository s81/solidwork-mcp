# SolidWorks MCP Server — Install Guide (Windows)

This package lets Claude (Claude Desktop, Claude Code or any MCP client) drive **SolidWorks 2022 or newer** on this PC: open, save and export files, read and write properties, build BOMs, model parts, and make drawings with dimensions, balloons, BOM tables and revision tables. `README.md` lists all 81 tools.

> **Status:** compiled against the SolidWorks 2022 SP5 API and covered by automated tests, but **not yet validated against a live SolidWorks session**. Try it on a test workstation with copies of your files before using it on production data.

## 1. Pick the right zip

| Zip | Use when | Needs |
|---|---|---|
| `solidworks-mcp-<version>-win-x64.zip` (self-contained, ~30 MB) | Most PCs. Nothing else to install. | Windows 10/11 x64 |
| `solidworks-mcp-<version>-win-x64-framework.zip` (~3 MB) | PCs that already have .NET 8, or where IT manages the runtime centrally | [.NET 8 Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0) ("Run desktop apps" or ".NET Runtime") |

Both need SolidWorks 2022 or newer, installed, licensed and activated on the **same** PC.

## 2. Install

1. Check the download: in PowerShell, `Get-FileHash .\solidworks-mcp-<version>-win-x64.zip` must match the value in `SHA256SUMS.txt` on the release page.
2. Right-click the zip → **Properties** → tick **Unblock** if shown → OK. This stops SmartScreen from blocking the exe.
3. Extract it to a fixed folder, e.g. `C:\Tools\solidworks-mcp\`. The folder needs `SolidWorksMcp.exe`.
4. Optional check: in PowerShell run `C:\Tools\solidworks-mcp\SolidWorksMcp.exe`. It should wait silently for input; this is normal (it talks over stdin/stdout). Press **Ctrl+C** to stop it.

## 3. Connect Claude

### Claude Desktop

1. Open `%APPDATA%\Claude\claude_desktop_config.json`. Create it if it doesn't exist (Claude Desktop → Settings → Developer → Edit Config).
2. Add the server, using `claude_desktop_config.example.json` from this zip as a template:

```json
{
  "mcpServers": {
    "solidworks": {
      "command": "C:\\Tools\\solidworks-mcp\\SolidWorksMcp.exe",
      "env": {
        "SWMCP_ALLOWED_ROOTS": "D:\\Engineering\\Projects;C:\\Exports",
        "SWMCP_ALLOW_MACROS": "false"
      }
    }
  }
}
```

   In JSON, backslashes must be doubled (`\\`). If the file already has other servers, add `"solidworks": {...}` inside the existing `mcpServers`.
3. Quit Claude Desktop completely (including from the system tray) and start it again. **solidworks** should appear in the tools (🔨) menu.

### Claude Code

```powershell
claude mcp add solidworks -e SWMCP_ALLOWED_ROOTS="D:\Engineering\Projects;C:\Exports" -- C:\Tools\solidworks-mcp\SolidWorksMcp.exe
```

## 4. Settings (environment variables in `env`)

| Variable | Default | What it does |
|---|---|---|
| `SWMCP_ALLOWED_ROOTS` | *(empty = any folder)* | `;`-separated folders. Every file a tool opens, saves or exports must be inside one of them. **Set this on shared or production PCs.** |
| `SWMCP_ALLOW_MACROS` | `false` | Enables `sw_run_macro`. A macro can run any code, so keep it off unless you need it. |
| `SWMCP_ATTACH_ONLY` | `false` | `true` = only use a SolidWorks that is already open; never start one. |
| `SWMCP_VISIBLE` | `true` | Show the SolidWorks window when the server starts SolidWorks. |
| `SWMCP_STARTUP_TIMEOUT_SECONDS` | `180` | How long to wait for SolidWorks to start. |
| `SWMCP_COM_RETRY_TIMEOUT_SECONDS` | `60` | How long to retry while SolidWorks is busy (rebuilds, dialogs). |

## 5. First test

With SolidWorks open, ask Claude:

1. *"Connect to SolidWorks and tell me the version."* → `sw_connect`
2. *"List the open documents."*
3. Open a **copy** of a part: *"Open C:\Exports\test\bracket.sldprt and give me its mass, material and custom properties."*
4. *"Export it to STEP AP214 in C:\Exports\test."*

`README.md` → **Verification** has the full 18-step smoke test, which covers modeling, sheet metal, weldments and drawings.

## 6. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Server doesn't show in Claude | Invalid JSON (check the `\\` and the commas), wrong path in `command`, or Claude wasn't fully restarted. Claude Desktop logs: `%APPDATA%\Claude\logs\mcp-server-solidworks.log`. |
| "Windows protected your PC" | The exe has no public code-signing certificate (see **Code signing** below). Unblock the zip before extracting (step 2.2), or ask IT to trust the signing certificate or allow `SolidWorksMcp.exe`. |
| Framework zip: "You must install .NET" | Install the .NET 8 Runtime x64, or use the self-contained zip. |
| "Could not connect to SolidWorks" | SolidWorks isn't installed, licensed or activated for this Windows user, or `SWMCP_ATTACH_ONLY=true` and SolidWorks isn't open. Start SolidWorks once by hand and close any licence dialogs. |
| Calls time out while SolidWorks shows a dialog | Close the dialog. The server retries for `SWMCP_COM_RETRY_TIMEOUT_SECONDS`. |
| "outside the allowed folders" | Add that folder to `SWMCP_ALLOWED_ROOTS` and restart Claude. |
| Claude and SolidWorks run as different users / elevation | COM can't cross user or elevation boundaries. Run both as the same user, and either both elevated or neither (not "Run as administrator" for only one). |

## 7. Code signing

When a release is signed, `SolidWorksMcp.exe` carries an Authenticode signature (SHA-256, timestamped) from the project's **self-signed, internal** certificate. The release notes give the certificate's thumbprint, and each zip, as well as the release page, contains the public certificate `SolidWorksMcp-signing.cer`.

Because the certificate is self-signed, Windows treats the signature as **untrusted** until IT deploys the certificate. After deployment, Windows shows the publisher as verified, and IT can allow the exe with an AppLocker or WDAC *publisher* rule instead of a file-hash rule. SmartScreen may still warn about a newly downloaded file, because a self-signed certificate has no public reputation. Unblocking the zip (step 2.2) still applies.

### Check a signature (anyone)

```powershell
Get-AuthenticodeSignature C:\Tools\solidworks-mcp\SolidWorksMcp.exe | Format-List Status, StatusMessage, SignerCertificate
```

The `SignerCertificate` thumbprint must equal the one in the release notes. Before IT deploys the certificate, `Status` reads `UnknownError` ("…root certificate which is not trusted"); after, it reads `Valid`.

### Trusting the signature (IT)

1. Check that the thumbprint of `SolidWorksMcp-signing.cer` matches the release notes: `(Get-PfxCertificate .\SolidWorksMcp-signing.cer).Thumbprint`.
2. Deploy it to **Trusted Root Certification Authorities** and **Trusted Publishers** on the PCs that run the server:
   - **Group Policy** (domain): *Computer Configuration → Policies → Windows Settings → Security Settings → Public Key Policies* → import the `.cer` under both **Trusted Root Certification Authorities** and **Trusted Publishers**.
   - **One PC** (elevated PowerShell):
     ```powershell
     Import-Certificate .\SolidWorksMcp-signing.cer -CertStoreLocation Cert:\LocalMachine\Root
     Import-Certificate .\SolidWorksMcp-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
     ```
3. Trust this certificate **only** for this project, and remove it if its private key is ever lost or leaked.

### Enabling signing (maintainer)

1. On a trusted Windows PC run `pwsh build/new-signing-cert.ps1`. It creates the certificate (RSA 3072, valid 5 years by default) and prints the thumbprint.
2. In GitHub go to **Settings → Secrets and variables → Actions** and add:
   - `SIGNING_CERT_PFX_BASE64`: the contents of `signing/solidworks-mcp-signing.pfx.b64`
   - `SIGNING_CERT_PASSWORD`: the password you chose
3. Delete the `.b64` file and keep the `.pfx` offline. The `signing/` folder is git-ignored.

From then on, every release run signs both exes, checks that each signature is valid and timestamped, and publishes `SolidWorksMcp-signing.cer`. Without the secrets, releases are built unsigned and the run shows a warning.

## 8. Update / uninstall

- **Update:** quit Claude, replace the folder contents with the new zip, and start Claude again.
- **Uninstall:** remove the `solidworks` entry from the Claude config and delete the folder. Nothing is written to the registry.
