# SolidWorks MCP Server (.NET 8)

A [Model Context Protocol](https://modelcontextprotocol.io) server that lets Claude (Claude Desktop, Claude Code, or any MCP client) drive a local **SolidWorks 2022+** installation: open/save/export files, read and write custom properties, build BOMs, sketch and extrude parts, edit dimensions and equations, create drawings, and batch-export STEP/PDF/flat-pattern DXF.

Built with the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol` 2.3.1) and the SolidWorks COM API, over stdio.

> **Status: v0.1.** The code compiles against the SolidWorks 2022 SP5 interop assemblies and the MCP protocol layer is covered by automated tests, but it has **not yet been run against a live SolidWorks session** in this repository's CI (no SolidWorks licence on CI runners). Validate on a test workstation with non-production files before rolling out. See [Verification](#verification).

## Requirements

| | |
|---|---|
| OS | Windows 10/11 x64 (SolidWorks is Windows-only) |
| SolidWorks | 2022 or newer, licensed and activated on the same machine |
| Runtime | .NET 8 runtime (or publish self-contained) |
| Build | .NET 8 SDK |

## Build & install

```bash
dotnet build -c Release
```

```bash
dotnet publish src/SolidWorksMcp -c Release -o C:\Tools\solidworks-mcp
```

Interop resolution: if `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll` exists, the build references the DLLs from your installation (the officially supported source). Otherwise it falls back to the `SolidWorks.Interop.sldworks` / `swconst` 30.5.1 NuGet packages (2022 SP5.1). If SolidWorks is installed elsewhere:

```bash
dotnet build -c Release -p:SolidWorksApiPath="D:\SOLIDWORKS\api\redist"
```

### Register with Claude Desktop

Add to `%APPDATA%\Claude\claude_desktop_config.json` (see [`examples/claude_desktop_config.json`](examples/claude_desktop_config.json)):

```json
{
  "mcpServers": {
    "solidworks": {
      "command": "C:\\Tools\\solidworks-mcp\\SolidWorksMcp.exe",
      "env": { "SWMCP_ALLOWED_ROOTS": "D:\\Engineering\\Projects;C:\\Exports" }
    }
  }
}
```

### Register with Claude Code

```bash
claude mcp add solidworks -e SWMCP_ALLOWED_ROOTS="D:\Engineering\Projects" -- C:\Tools\solidworks-mcp\SolidWorksMcp.exe
```

## Configuration (environment variables)

| Variable | Default | Purpose |
|---|---|---|
| `SWMCP_ALLOWED_ROOTS` | *(empty = no restriction)* | `;`-separated folders. Every path a tool reads or writes must be inside one. **Recommended in production.** |
| `SWMCP_ALLOW_MACROS` | `false` | Enables `sw_run_macro`. A macro is arbitrary code, so keep this off unless needed. |
| `SWMCP_ATTACH_ONLY` | `false` | Only attach to a running SolidWorks; never launch one. |
| `SWMCP_VISIBLE` | `true` | Show the SolidWorks window when the server launches it. |
| `SWMCP_STARTUP_TIMEOUT_SECONDS` | `180` | Max wait for a launched SolidWorks to finish starting. |
| `SWMCP_COM_RETRY_TIMEOUT_SECONDS` | `60` | How long to retry calls SolidWorks rejects while busy. |

## Tools (63)

Conventions: lengths in **mm**, angles in **degrees**, mass in **kg**. `document` accepts a full path, file name or window title; omit it to use the active document.

| Group | Tools |
|---|---|
| Connection | `sw_connect` |
| Documents | `sw_list_open_documents`, `sw_get_document_info`, `sw_open_document`, `sw_new_document`, `sw_activate_document`, `sw_save_document`, `sw_save_as`, `sw_close_document`, `sw_rebuild`, `sw_screenshot` (returns an image to the model) |
| Export | `sw_export` (STEP AP203/214/242, IGES, Parasolid, SAT, STL, 3MF, IFC, PDF, DXF/DWG, eDrawings, JPG/TIF/PNG), `sw_export_flat_pattern_dxf` (sheet metal), `sw_batch_export` (whole folder, incl. `flat-dxf`) |
| Properties | `sw_get_custom_properties`, `sw_set_custom_property`, `sw_delete_custom_property`, `sw_get_mass_properties`, `sw_get_material`, `sw_set_material`, `sw_list_configurations`, `sw_activate_configuration` |
| Assembly / BOM | `sw_get_component_tree`, `sw_get_bom` (`top_level` or `parts_only`, with chosen custom properties), `sw_insert_component`, `sw_add_mate` (coincident, concentric, parallel, perpendicular, tangent, distance, angle, lock), `sw_list_mates`, `sw_set_component_fixed` |
| Modeling | `sw_create_sketch` (line, centerline, rectangles, circle, arc, polygon, slot, point), `sw_extrude`, `sw_cut_extrude`, `sw_revolve` (boss or cut, around a sketch centerline), `sw_fillet` (constant radius; edges by feature, whole part, or nearest point), `sw_chamfer` (equal distance, two distances, or distance + angle; same edge selection), `sw_hole_wizard` (ISO clearance, tapped, counterbore, countersink; many holes in one feature), `sw_list_edges`, `sw_list_features`, `sw_set_feature_suppression`, `sw_delete_feature`, `sw_list_dimensions`, `sw_set_dimension` |
| Sheet metal | `sw_get_sheet_metal_info` (thickness, bend radius, K-factor, flattened state, cut-list flat size / area / cutting length / bends), `sw_base_flange` (closed or open profile), `sw_edge_flange`, `sw_set_flattened`, `sw_set_sheet_metal_parameters` |
| Weldments | `sw_list_weldment_profiles` (standard / type / size from the SolidWorks Weldment Profiles folder), `sw_add_structural_member` (members along sketch lines, corner treatment, rotation), `sw_get_weldment_cut_list` (LENGTH, ANGLE1/2, quantity, material, mass per member group), `sw_trim_extend`, `sw_add_end_cap`, `sw_add_gusset` |
| Equations | `sw_list_equations`, `sw_set_global_variable`, `sw_add_equation`, `sw_delete_equation` |
| Drawings | `sw_create_drawing` (1st/3rd angle, isometric, import model dimensions), `sw_list_drawing_sheets`, `sw_activate_sheet`, `sw_insert_bom_table` (top-level / parts-only / indented, template, anchor), `sw_read_bom_tables` (rows keyed by column title, all sheets), `sw_export_bom_table` (CSV / TSV) |
| Macros | `sw_run_macro` (disabled by default) |

### Example prompts

- *"Open D:\Projects\LabBench\LabBench.sldasm and give me a parts-only BOM with PartNo, Description and Material as a table."*
- *"Export every sheet-metal part in D:\Projects\Cabinet\Parts to flat-pattern DXF in C:\Exports\Laser."*
- *"Create a 600 × 400 × 3 mm plate on the top plane with four Ø9 mm holes 25 mm from each corner, set material AISI 304, and save it as C:\Work\plate.sldprt."*
- *"Make a turned spacer: on the front plane sketch a 20 × 5 mm rectangle 10 mm from a vertical centerline, revolve it 360°, then fillet the outer circular edges R1."*
- *"In LabBench.slddrw, add a parts-only BOM table at the top-right anchor, then export it to C:\Exports\LabBench-BOM.csv for purchasing."*
- *"Build a 1200 × 600 mm table frame from ISO square tube 40 × 40 × 4 with mitred corners, add 5 mm end caps to the legs, and give me a saw cutting list with lengths and angles."*
- *"Make a 2 mm sheet metal bracket: base flange from a 200 × 100 mm rectangle with R2 bends and K 0.44, add 40 mm 90° flanges on both long edges, then give me the flat blank size and bend count for a laser quote."*
- *"Add four M6 counterbored holes through the top face of the base plate, 20 mm in from each corner."*
- *"In LabBench.sldasm, fix Frame-1, then make the top plane of Top-1 coincident with the top plane of Frame-1 and mate its front plane 20 mm from Frame-1's front plane."*
- *"Set global variable Width to 1500 in the desk assembly, rebuild, and report the new mass."*
- *"Make a first-angle A3 drawing of bracket.sldprt with an isometric view and export it to PDF."*

## Architecture

```
MCP client ──stdio JSON-RPC──▶ SolidWorksMcp.exe
                                 ├─ ModelContextProtocol SDK (tool discovery, schemas, transport)
                                 ├─ Tools/*            one class per tool group, DTO in / DTO out
                                 ├─ SolidWorksSession  connect / attach / reconnect, error mapping
                                 └─ StaDispatcher      single STA thread + OLE message filter
                                                          │ COM (out-of-proc)
                                                          ▼
                                                     SLDWORKS.exe
```

Design decisions:

- **Single STA thread.** The SolidWorks API is apartment-threaded and not re-entrant. All calls are queued onto one STA thread; concurrent MCP requests are serialized. COM objects never leave that thread — tools return plain records.
- **OLE message filter.** When SolidWorks is busy (rebuild, modal dialog) COM calls are retried for up to `SWMCP_COM_RETRY_TIMEOUT_SECONDS` instead of failing with `RPC_E_CALL_REJECTED`.
- **Attach first, launch second.** `GetActiveObject` is re-implemented via `oleaut32` (removed from .NET Core). The server never calls `ExitApp` — SolidWorks belongs to the user.
- **Reconnect.** A liveness ping precedes each call; if SolidWorks was closed or crashed, the next call reconnects.
- **SI ↔ engineering units** at the tool boundary (the API is always metres/radians).
- **Localization-safe planes.** `front/top/right` map to the first three reference planes in the tree rather than English names.
- **stdout is the protocol.** All logging goes to stderr.
- **Safety.** Path allow-list, macros opt-in, read-only opens for batch jobs, no overwrite on `sw_save_as` unless asked, and MCP tool annotations (`readOnly`, `destructive`, `idempotent`) so clients can require confirmation.

## Verification

What is verified automatically (`dotnet test`, 273 tests, runs on Windows and Linux):

- Compiles against the real SolidWorks 2022 interop, so every API member and argument count used exists.
- MCP end-to-end: the built server is launched over stdio, tools are listed with JSON schemas and annotations, errors are returned as tool errors and the server stays healthy.
- Path allow-list (incl. `..` traversal and prefix tricks), configuration parsing, STA dispatcher (single thread, serialization, exceptions, cancellation), BOM aggregation, error-flag decoding, unit conversions, fillet/chamfer edge de-duplication and nearest-edge picking, chamfer parameter rules, mate type/alignment parsing, mate reference validation, component name matching, Hole Wizard type/size/depth rules and point-on-plane checks, sheet metal parameter/flange rules, cut-list number parsing, weldment profile library scanning/lookup, corner/trim/gusset option parsing and body/segment name matching, BOM table option parsing, template choice and table-grid-to-rows conversion.

What still needs a SolidWorks workstation (recommended manual smoke test):

1. `sw_connect` → version reported.
2. New part → `sw_create_sketch` (center rectangle) → `sw_extrude` 10 mm → `sw_list_dimensions` → `sw_set_dimension` → `sw_screenshot`.
3. `sw_export` to `.step` and open in another CAD viewer.
4. Open a real assembly → `sw_get_bom` and compare with a drawing BOM table.
5. `sw_export_flat_pattern_dxf` on a sheet-metal part; check bend lines in the DXF.
6. `sw_create_drawing` first-angle → `sw_export` PDF.
7. Sketch a closed profile plus a `centerline` → `sw_revolve` 360° and 90°, then `cut: true`.
8. `sw_list_edges` on an extrude → `sw_fillet` with `selection: points` (edge midpoints), then `selection: feature, kind: circle`.
9. `sw_chamfer` 1 mm `equal_distance` on the top edges of a block, then `angle_distance` 2 mm × 30° with and without `flip` (check which face the 2 mm lands on).
10. In a two-part assembly: `sw_set_component_fixed` the first part → `sw_add_mate` coincident plane-to-plane (`feature: top`), then distance 20 mm, then concentric between two cylindrical faces picked by `entity: face` + `point` → `sw_list_mates`.
11. On a 100 × 60 × 15 mm block: `sw_hole_wizard` counterbore M6 at two points on the top face, tapped M8 blind 20 mm (thread 15 mm), countersink M5 through all; compare the reported dimensions with the Hole Wizard PropertyManager.
12. Sheet metal: `sw_create_sketch` a 200 × 100 rectangle on top → `sw_base_flange` 2 mm, R2, K 0.44 → `sw_list_edges` → `sw_edge_flange` 40 mm 90° on both long edges → `sw_get_sheet_metal_info` (compare bounding box and Bends with the Cut-List Properties dialog) → `sw_set_flattened` true/false → `sw_set_sheet_metal_parameters` thickness 3 → `sw_export_flat_pattern_dxf`.
13. Weldment: `sw_list_weldment_profiles` → 3D or 2D sketch of a rectangular frame plus four vertical legs → `sw_add_structural_member` square tube for the frame (miter) and again for the legs → `sw_get_weldment_cut_list` (compare with the Cut-List Properties dialog) → `sw_trim_extend` the legs to the frame → `sw_add_end_cap` on the leg bottoms → `sw_add_gusset` in one corner.
14. Drawing BOM: `sw_create_drawing` of an assembly → `sw_insert_bom_table` parts_only (default template) → `sw_read_bom_tables` (compare with the table on the sheet and with `sw_get_bom`) → `sw_export_bom_table` to .csv and open it in Excel.

Known limitations / roadmap: no advanced/mechanical mates (width, symmetric, gear, slot, limit mates), vertex chamfers, variable-radius or face fillets, non-ISO Hole Wizard standards (ANSI, DIN, JIS...), sheet metal hems / miter flanges / sketched bends / gauge tables / convert-to-sheet-metal, weld beads / weld tables / polygonal or chamfered gussets / multiple member groups per feature, thin revolves, or face selection yet; `sw_get_bom` computes the BOM from the assembly tree, while the drawing tools read the BOM table on the sheet (no editing of table cells or columns yet); `sw_insert_component` places components floating (fix or mate them afterwards).

## Development

```bash
dotnet test
```

The project targets `net8.0-windows` with `EnableWindowsTargeting`, so it builds and its tests run on Linux/macOS; SolidWorks calls return a clear tool error there.

## License

The SolidWorks interop assemblies are © Dassault Systèmes and are subject to your SolidWorks licence. The NuGet fallback packages are third-party repackagings (publisher: *avidesk*); prefer your installation's `api\redist` DLLs for production builds.
