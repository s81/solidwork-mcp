# CLAUDE.md — working rules for this repo

SolidWorks MCP server: .NET 8 (`net8.0-windows` with `EnableWindowsTargeting`), the official MCP C# SDK
(`ModelContextProtocol` 2.3.1) over stdio, and SolidWorks 2022+ interop (NuGet `SolidWorks.Interop.*` 30.5.1).

**Read [docs/HANDOFF.md](docs/HANDOFF.md) first.** It covers the current state, the feature backlog, and the
checklist for adding a tool.

## Build and test (Linux is fine)

```bash
dotnet build
dotnet test
```

- Everything builds and the unit tests run on Linux. SolidWorks calls only work on Windows with SolidWorks installed.
- There is no PowerShell locally, so `build/*.ps1` and the workflows are only proven in CI (`windows-latest`).
- Nothing has been run against a live SolidWorks yet. Never claim a tool "works in SolidWorks"; say it compiles and its unit tests pass.

## Architecture rules (do not break)

- **STA only.** All COM calls run through `SolidWorksSession.RunAsync` / `RunOnDocumentAsync`, which use one STA thread
  (`Infrastructure/StaDispatcher.cs`). Never touch a COM object outside that delegate, and never return one: return plain records/DTOs.
- **Spec vs Tools split.** Pure logic (parsing, validation, unit conversion, picking) goes in `SolidWorks/<Feature>Spec.cs`
  and is unit-tested. `Tools/<Feature>Tools.cs` only maps arguments → Spec → COM calls.
- **Units at the boundary.** Tool arguments are mm / degrees / kg. The SolidWorks API uses metres / radians, so convert with `Infrastructure/Units.cs`.
- **Errors.** Throw `McpException` with a message that tells the model what to do next (for example, "call sw_list_balloons first").
- **Paths.** Any file a tool reads or writes goes through `PathGuard` (`SWMCP_ALLOWED_ROOTS`).
- **stdout is the JSON-RPC stream.** Log to stderr only (already configured in `Program.cs`).
- **Tool attributes.** Names are `sw_<snake_case>`. Set `ReadOnly` / `Destructive` / `Idempotent` honestly. Use `OpenWorld = false`
  (only `sw_run_macro` uses true). Every parameter gets a `[Description]` that states its units.
- **Interop signatures:** check them by reflection, not memory. The recipe is in HANDOFF.md §5. Indexed COM properties need
  `set_X(index, value)` (CS0856 otherwise).

## Delivery process (one feature = one PR)

1. Branch: develop on the session's designated branch, freshly reset from main:
   `git fetch origin main && git checkout -B <branch> origin/main`.
2. Implement, then update **every** tool-count reference (listed in HANDOFF.md §4).
3. `dotnet test` must be green locally, then push and open a **draft** PR.
4. The user says "mark ready" / "merge". Squash-merge with the full 40-character head SHA.
5. Never put a model ID in commits, PRs or code.

## Releases

- The session git proxy blocks **tag pushes (403)**. Release by running the `Release` workflow (`workflow_dispatch`,
  inputs `version`, `publish=true`). `gh release create` makes the tag.
- First bump `<Version>` in `src/SolidWorksMcp/SolidWorksMcp.csproj` and add a "What's new" section to `docs/RELEASE_NOTES.md`.
  The workflow refuses a version that doesn't match the csproj.
- After publishing, run `Mark release superseded` (`supersede-release.yml`) on the previous tag.
