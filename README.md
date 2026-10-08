# SsmsSqlHelper

A SQL Prompt-style add-in for **SQL Server Management Studio 22** (VSIX). Snippets, table/column/join suggestions, automatic aliases, safety warnings and a backup of unsaved query tabs.

- SSMS 22 only (VS 2026 shell, .NET Framework 4.8)
- SQL Authentication connections
- Everything runs locally; the only database access is read-only queries against `sys.objects`, `sys.columns` and `sys.foreign_keys` on the connection of the active query window

> Status: in development. Not every feature has been verified inside SSMS yet (see [Known uncertainties](#known-uncertainties)).

## Features

| Area | What it does |
|---|---|
| **Snippets** | Type a shortcut (`ssf`, `st100`, `ii`, `uu`, ...) and press **Tab**. A suggestion list explains what the shortcut will insert. Supports tab stops (`$1`, `${1:text}`, `$0`), `$CURSOR$`, `$SELECTED$` and variables (`$DATE$`, `$TIME$`, `$DATETIME$`, `$USER$`, `$MACHINE$`, `$SERVER$`, `$DATABASE$`, `$CLIPBOARD$`). `$$` is a literal `$`. |
| **Context Tab expansion** | `SELECT * FROM Budgets` + Tab on the `*` expands to the column list (a picker with *Select all* appears). `INSERT INTO Table` + Tab generates the column list. |
| **Table suggestions** | After `FROM` / `JOIN` / `INSERT INTO` / `UPDATE` and so on. |
| **Join suggestions** | After `JOIN x` the `ON` condition is suggested from foreign keys, then from naming convention (`BudgetId` -> `Budgets.Id`) because many schemas do not declare every FK. |
| **Automatic aliases** | `Budgets` -> `b`, `BudgetLines` -> `bl` (capital letters), numbered on collision, reserved words avoided. |
| **Column suggestions** | In SELECT, WHERE, ON, ORDER BY and after `alias.`. Columns carry the alias when the query has one. Reopens after Backspace/Delete. |
| **Keyword suggestions** | **Ctrl+Space** (or Ctrl+J) offers what normally comes next (WHERE, GROUP BY, ...). Follows the letter case of the script. |
| **WHERE warning** | Warns before an `UPDATE` / `DELETE` without `WHERE` is executed. |
| **Connection banner** | Server and database in large letters at the top of the query window, coloured by rule (default: names containing `prod` are red). |
| **Backup of unsaved tabs** | A copy of unsaved query text is written 3 s after typing stops. After SSMS crashes or is ended from Task Manager, the next start offers to recover the tabs. |
| **Metadata cache** | Loaded per server/database/user in the background, refreshed automatically when the schema fingerprint changes. |

Menu: **Tools -> SQL Helper** contains Show Active Connection, Edit Snippets, Surround With Snippet..., Recover Unsaved Queries... and Refresh Metadata.

### Snippets

Edit them in **Tools -> SQL Helper -> Edit Snippets** (also holds the on/off switches for each feature and the banner colour rules). The source of truth is `%AppData%\SsmsSqlHelper\snippets.json`, one snippet per line; the built-in set is in `src/SsmsSqlHelper/Snippets/DefaultSnippets.json`.

### Settings

`%AppData%\SsmsSqlHelper\settings.json`: `showSnippetHints`, `showColumnHints`, `showKeywordHints`, `autoAlias`, `warnMissingWhere`, `backupUnsavedTabs`, `showConnectionBanner`, `connectionColors`.

### Backup of unsaved tabs

- Copies live in `%AppData%\SsmsSqlHelper\Backups\<session>\`; each running SSMS holds an exclusive `.lock` in its folder, so several SSMS windows never touch each other's copies and the OS releases the lock however the process ends.
- Saving or closing a tab normally removes its copy.
- Recovered tabs are written to `%AppData%\SsmsSqlHelper\Recovered\` and opened unconnected.
- Copies older than 14 days or identical to the saved file are dropped.
- Query text is stored unencrypted in the user profile while a tab is unsaved.

## Repository layout

```
SsmsSqlHelper.sln
src/SsmsSqlHelper/          the VSIX
  Backup/                   unsaved-tab backup (store, service, tracker)
  Commands/                 menu commands, ExecuteGuard (WHERE warning)
  Editor/                   MEF completion sources, command filter, banner margin
  Generation/               pure generators: aliases, joins, columns, context expansion
  Metadata/                 loaders, cache, DbMetadata
  Parsing/                  T-SQL tokenizer and context analysis (no VS types)
  Settings/ Snippets/ Ssms/ UI/ Diagnostics/
tests/SsmsSqlHelper.Tests/  MSTest (net48); links the pure source files
scripts/dev-install.ps1     install / uninstall into the local SSMS 22
```

Logic that does not need Visual Studio lives in `Parsing/`, `Generation/`, `Snippets/` and `Backup/BackupStore.cs`, so it is unit-tested without SSMS. Only `Ssms/SsmsConnectionAdapter.cs` touches SSMS internals.

## Building

Requirements: Visual Studio 2026 (or its Build Tools) with the *Visual Studio extension development* workload, SSMS 22 installed (its DLLs are referenced from the install directory, not copied).

```powershell
msbuild SsmsSqlHelper.sln /restore
dotnet test tests\SsmsSqlHelper.Tests
```

The VSIX is produced at `src\SsmsSqlHelper\bin\Debug\SsmsSqlHelper.vsix`.

## Installing for development

SSMS has no experimental instance. Close SSMS, then:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dev-install.ps1            # Debug build
powershell -ExecutionPolicy Bypass -File scripts\dev-install.ps1 -Uninstall
```

If menus do not appear after an install, close SSMS completely and run once:

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
```

If the package fails to load, SSMS names `%AppData%\Microsoft\SSMS\22.0_*\ActivityLog.xml` in its error; search it for `SsmsSqlHelper`. The add-in's own log is the *SQL Helper* pane of the Output window.

## Known uncertainties

Not yet confirmed inside SSMS: the banner margin in query windows, `Query.Execute` hooking for the WHERE warning (the command name is logged if the lookup fails), Tab accepting soft-selected suggestions, and `ITextDocument.IsDirty` for query buffers (the backup tracker logs it if no document is found).

## Roadmap

Installer for the team, shared team snippet file, alias overrides (e.g. `Budgets = bg`), production-server warning on execute, `EXEC` parameter generation, INSERT from data, SQL formatting.
