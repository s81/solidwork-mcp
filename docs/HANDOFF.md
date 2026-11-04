# Hand-off — SolidWorks MCP server

Last updated **2026-11-04**, after release **v0.1.2** (main at `576ea03`). This is for the next session, which adds features.
Working rules are in [`CLAUDE.md`](../CLAUDE.md).

## 1. Where things stand

| Item | State |
|---|---|
| Tools | **81**, in 18 groups (§3) |
| Tests | **415** passing (`dotnet test`, Linux and Windows CI) |
| Latest release | **v0.1.2**: unsigned, win-x64, self-contained (~32 MB) and framework-dependent (~2.8 MB) zips, `SHA256SUMS.txt` |
| Older releases | v0.1.1 and v0.1.0 are marked "superseded" and pre-release |
| Open PRs | none |
| Licence | MIT © 2026 Samer William Al-Haddadin. `THIRD-PARTY-NOTICES.md` covers the dependencies |

### Unresolved risks (state these honestly to the user)

1. **No live SolidWorks validation yet.** Every COM call is compiled against the interop and its signature was checked by reflection,
   but nothing has run in SolidWorks. README "Verification" (smoke steps) still needs a run on a Windows PC with SolidWorks.
   Expect fixes to come from that run.
2. **Releases are unsigned.** To sign them, run `pwsh build/new-signing-cert.ps1` on a trusted PC, then add the repository secrets
   `SIGNING_CERT_PFX_BASE64` and `SIGNING_CERT_PASSWORD`. Without these the workflow skips signing and warns.
3. **Interop redistribution.** The zips bundle the SolidWorks interop DLLs (NuGet repackaging by *avidesk*). Confirm with
   Dassault / your reseller that redistributing them is allowed before giving zips to anyone outside your organisation.
4. **README has two `## License` headings** (the first is really "SolidWorks interop licensing"). This is a small cleanup to do in any doc PR.

## 2. Repo map

```
src/SolidWorksMcp/
  Program.cs                 host, DI, MCP server (version from ServerVersion.Current)
  ServerInstructions.cs      instructions sent to the model: add a line per new feature group
  Infrastructure/            StaDispatcher (STA thread), OleMessageFilter (busy retry), PathGuard,
                             ServerOptions (SWMCP_* env vars), Units (mm/deg <-> m/rad), ComInterop
  SolidWorks/                SolidWorksSession (RunAsync / RunOnDocumentAsync), Documents, *Spec.cs (pure logic)
  Tools/                     *Tools.cs, [McpServerToolType] classes, the only place tools are declared
tests/SolidWorksMcp.Tests/   xUnit: one *SpecTests per Spec, plus ToolCatalogTests and McpProtocolTests
build/                       package.ps1 (zips + optional signing), smoke-test.ps1, new-signing-cert.ps1
.github/workflows/           ci.yml, release.yml, supersede-release.yml
docs/                        INSTALL.md (goes in the zip), RELEASE_NOTES.md (release body), HANDOFF.md
```

## 3. Tool inventory (81)

| File | Tools |
|---|---|
| ConnectionTools | sw_connect |
| DocumentTools | sw_list_open_documents, sw_get_document_info, sw_open_document, sw_new_document, sw_activate_document, sw_save_document, sw_save_as, sw_close_document, sw_rebuild, sw_screenshot |
| ExportTools | sw_export, sw_export_flat_pattern_dxf, sw_batch_export |
| PropertyTools | sw_get/set/delete_custom_property(ies), sw_get_mass_properties, sw_get/set_material, sw_list/activate_configuration(s) |
| AssemblyTools | sw_get_component_tree, sw_get_bom, sw_insert_component |
| MateTools | sw_add_mate, sw_list_mates, sw_set_component_fixed |
| ModelingTools | sw_create_sketch, sw_extrude, sw_cut_extrude, sw_list_features, sw_set_feature_suppression, sw_delete_feature, sw_list_dimensions, sw_set_dimension |
| FeatureTools | sw_revolve, sw_fillet, sw_chamfer, sw_list_edges |
| HoleTools | sw_hole_wizard |
| SheetMetalTools | sw_get_sheet_metal_info, sw_base_flange, sw_edge_flange, sw_set_flattened, sw_set_sheet_metal_parameters |
| WeldmentTools | sw_list_weldment_profiles, sw_add_structural_member, sw_get_weldment_cut_list, sw_trim_extend, sw_add_end_cap, sw_add_gusset |
| EquationTools | sw_list_equations, sw_set_global_variable, sw_add_equation, sw_delete_equation |
| DrawingTools | sw_create_drawing, sw_list_drawing_sheets, sw_activate_sheet |
| DrawingDimensionTools | sw_insert_model_dimensions, sw_list_drawing_dimensions, sw_auto_dimension_view, sw_add_drawing_dimension |
| BomTableTools | sw_insert_bom_table, sw_read_bom_tables, sw_export_bom_table, sw_renumber_bom_items |
| BalloonTools | sw_auto_balloon, sw_list_balloons, sw_add_balloon, sw_add_stacked_balloon, sw_edit_balloons, sw_delete_balloons, sw_add_magnetic_line |
| RevisionTableTools | sw_insert_revision_table, sw_add_revision, sw_read_revision_tables, sw_export_revision_table, sw_edit_revision, sw_delete_revision |
| MacroTools | sw_run_macro (off unless `SWMCP_ALLOW_MACROS=true`) |

## 4. Checklist: adding a feature

1. **Ask first** if the scope is unclear. The user prefers multiple-choice questions (AskUserQuestion) over assumptions.
2. **Probe the interop** for every method and enum you'll call (§5).
3. Add `SolidWorks/<Feature>Spec.cs` (pure: parse/validate/convert) and `tests/.../<Feature>SpecTests.cs`.
4. Add `Tools/<Feature>Tools.cs` (or extend an existing one): `sw.RunOnDocumentAsync(document, (app, doc) => ...)`, check
   the document type, convert units, return a record, and make `McpException` messages say what to call next.
5. Update **all** tool-count and catalogue references (CI fails or the docs go stale otherwise):
   - `tests/SolidWorksMcp.Tests/ToolCatalogTests.cs`: add the names to the `HasExpectedToolGroups` list
   - `build/smoke-test.ps1`: `[int]$MinTools = 81` default
   - `README.md`: `## Tools (81)` heading and the group table row
   - `docs/INSTALL.md` line 3 ("lists all 81 tools")
   - `docs/RELEASE_NOTES.md` line 1 ("81 tools") plus a "What's new" entry at release time
   - `src/SolidWorksMcp/ServerInstructions.cs`: a usage hint if the tool has an order or prerequisites
6. Add argument-validation cases to `McpProtocolTests` (bad enum / out-of-range values must return a clear error).
7. `dotnet test`, commit, push, open a draft PR (process in CLAUDE.md).

## 5. Interop probe recipe (check signatures before coding)

The previous session's probe lived in a scratch directory and is gone. Recreate it outside the repo:

```bash
mkdir -p /tmp/probe && cd /tmp/probe
cat > probe.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><NoWarn>NU1701</NoWarn></PropertyGroup>
<ItemGroup><PackageReference Include="SolidWorks.Interop.sldworks" Version="30.5.1" /><PackageReference Include="SolidWorks.Interop.swconst" Version="30.5.1" /></ItemGroup></Project>
EOF
cat > Program.cs <<'EOF'
using System.Reflection;
var asm = typeof(SolidWorks.Interop.sldworks.IFeatureManager).Assembly;
var casm = typeof(SolidWorks.Interop.swconst.swChamferType_e).Assembly;
void Dump(string t, params string[] names){ var ty=asm.GetType("SolidWorks.Interop.sldworks."+t); if(ty==null){Console.WriteLine("no "+t);return;}
  foreach(var m in ty.GetMembers().Where(m=>names.Length==0||names.Any(n=>m.Name.Contains(n)))){
    if(m is MethodInfo mi && !mi.IsSpecialName) Console.WriteLine($"{t}.{mi.Name}({string.Join(", ", mi.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name))}) : {mi.ReturnType.Name}");
    else if(m is PropertyInfo pi) Console.WriteLine($"{t}.{m.Name} [{pi.PropertyType.Name}]"); } }
void E(string n, string? f=null){ var t=casm.GetType("SolidWorks.Interop.swconst."+n);
  Console.WriteLine(n+": "+(t==null?"missing":string.Join(",", Enum.GetValues(t).Cast<object>().Select(v=>v+"="+(int)v).Where(s=>f==null||s.Contains(f))))); }
// Edit below, e.g.:
Dump("IFeatureManager", "FeatureLinearPattern", "FeatureCircularPattern");
E("swFeatureNameID_e");
EOF
dotnet run
```

## 6. Feature backlog (candidates, **not prioritised**)

Ask the user to pick and order these. They are grouped by fit with the group's work (steel structures; lab, office and educational furniture).

| Area | Candidate tools | Why it may matter |
|---|---|---|
| Drawing views | section, detail, projected, auxiliary, broken views; view scale/display style | Fabrication drawings need more than the standard 3 views |
| Annotations | notes, centre marks / centrelines, surface finish, weld symbols, GD&T / datum, hole table | Shop drawings for steel / welded frames |
| Title block | fill sheet-format properties from custom properties; sheet size/format change | Consistent drawing release |
| Patterns & features | linear / circular pattern, mirror, shell, reference plane / axis, sweep, loft | Common modelling gaps today |
| Sheet metal | miter flange, hem, sketched bend, jog, convert-to-sheet-metal, bend table / gauge table | Furniture panels and enclosures |
| Weldments | cut-list export to CSV/XLSX with lengths, weld bead, custom cut-list properties | Steel cutting lists for production |
| Assembly | component pattern, mirror component, interference detection, replace component, exploded view | Furniture assemblies and checks |
| Toolbox / fasteners | insert Toolbox hardware, Smart Fasteners | Hardware BOM accuracy |
| Configurations | create configuration, design-table read/write | Product families (sizes / variants) |
| Batch / release | drawing PDF package, Pack and Go, batch property update across a folder, DXF per sheet-metal body | Release to production and suppliers |
| Quality | cosmetic thread, measure tool (distance/angle between entities), check (geometry errors) | Verification without opening the UI |

## 7. How the user likes to work

- Direct, evidence-based answers. State unverified items plainly. Ask when a request is ambiguous; don't assume.
- One feature per PR. The user drives the steps with "Mark the PR ready for review" and "Merge the PR".
- After merging, the branch is reset from main for the next feature (merged PRs are never reused).
