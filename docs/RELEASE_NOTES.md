SolidWorks MCP server for **SolidWorks 2022 or newer** on Windows x64: 84 tools over stdio, built with .NET 8 and the official MCP C# SDK.

## What's new in 0.1.2

- **Code signing:** release builds can now Authenticode-sign `SolidWorksMcp.exe`, using SHA-256 with a timestamp and the project's self-signed internal certificate. Each signed release includes the public certificate `SolidWorksMcp-signing.cer`. The **Code signing** section at the end of these notes says whether this release is signed and gives the certificate thumbprint. `INSTALL.md` explains how to check a signature and how IT can trust it.
- **Release maintenance:** a new **Mark release superseded** workflow flags older releases and points them to the newer one.
- The tools are unchanged from 0.1.1.

## Downloads

| File | For |
|---|---|
| `solidworks-mcp-<version>-win-x64.zip` | **Most users.** Self-contained; no .NET install needed. |
| `solidworks-mcp-<version>-win-x64-framework.zip` | PCs that already have the .NET 8 Runtime x64. |
| `SHA256SUMS.txt` | Checksums. Check with `Get-FileHash <zip>`. |

Each zip has `SolidWorksMcp.exe`, `INSTALL.md` (setup, settings, troubleshooting), `README.md` (all tools, architecture, smoke test), `LICENSE.txt` (MIT), `THIRD-PARTY-NOTICES.md` and `claude_desktop_config.example.json`.

## Tools

- **Documents and export:** open/save/close, STEP/IGES/Parasolid/STL/PDF/DXF export, batch export, flat-pattern DXF
- **Properties and BOM:** custom properties, mass, material, configurations, component tree, BOM
- **Modeling:** sketches, extrude/cut, revolve, fillet, chamfer, Hole Wizard (ISO), equations
- **Assembly:** insert components, mates, fix/float
- **Sheet metal:** base/edge flange, flatten, parameters, cut-list info
- **Weldments:** profiles, structural members, cut list, trim/extend, end caps, gussets
- **Drawings:** create drawings, sheets, BOM tables, model and manual dimensions, balloons (auto, stacked, edit, delete, magnetic lines, renumber), revision tables
- **Macros:** opt-in via `SWMCP_ALLOW_MACROS`

## Status and limits

- Compiled against the SolidWorks 2022 SP5 API and covered by automated tests. Before every release, each zip is started and the MCP handshake, server version and tool list are checked.
- **Not yet validated against a live SolidWorks session.** Run the README smoke test on a test workstation first.
- Code signing: see the **Code signing** section below and `INSTALL.md`. Unblock the zip before extracting either way.
