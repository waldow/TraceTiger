# 🐯 TraceTiger

A dark-themed desktop workbench for **managing and running KQL scripts against Azure** —
Application Insights resources and Log Analytics workspaces. Think "Postman for KQL":
a library of `.kql` files, syntax-highlighted editor tabs, one-click runs, and results as
grid, chart, or JSON.

Built with WPF on **.NET 10** (`net10.0-windows`).

## Features

- **Script library** — plain `.kql` files in a folder you choose (git-friendly). Shown as a
  tree with folders; create / rename / delete / reveal-in-Explorer from the context menu.
  External edits are picked up automatically via a file watcher.
- **Script metadata in header comments** (all optional, still valid KQL):
  ```kql
  // @name Slow dependencies for a service
  // @description Dependencies slower than the threshold for one cloud role.
  // @param SERVICE-NAME checkout-api
  dependencies
  | where cloud_RoleName == "<SERVICE-NAME>"
  ```
- **Editor tabs** — AvalonEdit with custom KQL syntax highlighting (keywords, functions,
  known tables, strings, `<PLACEHOLDER>` tokens), dirty tracking, session restore across
  restarts. `Ctrl+T/W/Tab` tab management, `Ctrl+S` save, `F5`/`Ctrl+Enter` run.
- **Parameters** — `<TOKEN>` placeholders (and `@param` declarations) become an input strip
  above the results; values are remembered across runs and KQL-escaped on substitution.
- **Connections** — named targets with a color badge: App Insights resource (ARM id),
  Log Analytics workspace (GUID or ARM id), or the built-in **Demo** connection that returns
  canned data so every feature works without Azure. Sign-in is an interactive Entra browser
  prompt with a persisted token cache (no secrets stored, access via your RBAC).
- **Time range picker** — query-defined (the script's own `ago()` filters), presets from
  15 minutes to 30 days, or a custom from/to window — applied server-side.
- **Results** — auto-typed DataGrid, LiveCharts2 line/column charts (auto-projected;
  a trailing `| render timechart` preselects the chart), pretty-printed JSON view, and a
  cell inspector (double-click any cell — JSON cells are formatted). Export CSV, copy TSV
  (pastes into Excel), copy the resolved KQL.
- **History** — the last 200 runs with row count, duration, and errors; double-click to
  reopen any run in a scratch tab.

## Build & run

Requires the .NET 10 SDK on Windows:

```
dotnet build TraceTiger.sln
dotnet run --project Source/TraceTiger.csproj
```

From WSL, use the Windows SDK: `"/mnt/c/Program Files/dotnet/dotnet.exe" build TraceTiger.sln`
(the Linux dotnet cannot run WPF, and mixing the two SDKs churns `obj/` — delete `obj/` if switched).

## First run

1. Pick a scripts folder (the repo's `Samples/` works) via the left panel or Settings.
2. The seeded **Demo** connection runs any query against canned data — try
   `Samples/requests-over-time.kql` with F5.
3. Add a real connection in **Settings**: paste the App Insights ARM resource id or the
   Log Analytics workspace id, then **Sign in & test** (opens a browser; the token is cached
   so the next launches won't prompt).

## Data locations

- Settings, run history, open tabs, remembered parameter values:
  `%AppData%\TraceTiger\{settings,history,tabs,paramvalues}.json`
  (camelCase JSON, atomic writes, corruption-tolerant loads).
- Scripts: wherever you point the library — nothing is copied.

## Architecture (short version)

Single WPF project, MVVM with CommunityToolkit.Mvvm source generators, services wired as
static singletons on `App`. `Models/` and the pure services (`QueryParameterResolver`,
`ChartProjector`, `CsvExporter`, `JsonResultFormatter`, `ScriptMetadataParser`) never touch
the Azure SDK — only `KustoQueryService` does (`Azure.Monitor.Query`, one `LogsQueryClient`
per tenant on an `InteractiveBrowserCredential` with a DPAPI-persisted MSAL cache).
Results flow through a provider-agnostic `QueryResultTable` into the grid/chart/JSON projections.

Packages: Azure.Identity, Azure.Monitor.Query, AvalonEdit, CommunityToolkit.Mvvm,
LiveChartsCore.SkiaSharpView.WPF.
