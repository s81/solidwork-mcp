SolidWorks MCP server for **SolidWorks 2022 or newer** on Windows x64: 84 tools over stdio, built with .NET 8 and the official MCP C# SDK.

## What's new in 0.1.3

- **Section views:** `sw_add_section_view` cuts a drawing view with a horizontal, vertical or angled line through a point. Options: aligned or not, flip direction, partial, surface only, depth limit, exclude fasteners and label. By default the view is placed beside its parent.
- **Detail views:** `sw_add_detail_view` enlarges a circular area of a drawing view. The default scale is twice the parent's. You can set the scale, style, circle display, full or jagged outline, and label.
- **Listing:** `sw_list_section_detail_views` returns each section and detail view with its parent, label, scale, position, outline and settings.
- If you don't give a label, the next unused letter is used (I, O and Q are skipped).
- 81 → 84 tools. All other tools are unchanged from 0.1.2.

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
- **Drawings:** create drawings, sheets, BOM tables, model and manual dimensions, section and detail views, balloons (auto, stacked, edit, delete, magnetic lines, renumber), revision tables
- **Macros:** opt-in via `SWMCP_ALLOW_MACROS`

## Status and limits

- Compiled against the SolidWorks 2022 SP5 API and covered by automated tests. Before every release, each zip is started and the MCP handshake, server version and tool list are checked.
- **Not yet validated against a live SolidWorks session.** Run the README smoke test on a test workstation first. For the new view tools, check that cut lines and detail circles land where requested, including on rotated views and on sheets that aren't 1:1.
- Code signing: see the **Code signing** section below and `INSTALL.md`. Unblock the zip before extracting either way.
