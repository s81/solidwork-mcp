First release of the SolidWorks MCP server for **SolidWorks 2022 or newer** on Windows x64: 81 tools over stdio, built with .NET 8 and the official MCP C# SDK.

## Downloads

| File | For |
|---|---|
| `solidworks-mcp-<version>-win-x64.zip` | **Most users.** Self-contained; no .NET install needed. |
| `solidworks-mcp-<version>-win-x64-framework.zip` | PCs that already have the .NET 8 Runtime x64. |
| `SHA256SUMS.txt` | Checksums. Check with `Get-FileHash <zip>`. |

Each zip has `SolidWorksMcp.exe`, `INSTALL.md` (setup, settings, troubleshooting), `README.md` (all tools, architecture, smoke test) and `claude_desktop_config.example.json`.

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

- Compiled against the SolidWorks 2022 SP5 API and covered by 409 automated tests. Before every release, each zip is started and the MCP handshake and tool list are checked.
- **Not yet validated against a live SolidWorks session.** Run the README smoke test on a test workstation first.
- `SolidWorksMcp.exe` is **not code-signed**. Unblock the zip before extracting (see `INSTALL.md`).
