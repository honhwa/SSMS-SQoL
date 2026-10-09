<img width="1574" height="687" alt="ssms-sqol-logo" src="https://github.com/user-attachments/assets/19c5b612-62b6-474c-bc02-b4f8cb55f343" />

# SQL Helper for SSMS 22

SQL Helper is a VSIX extension for **SQL Server Management Studio 22**. It adds SQL completion, snippets, object navigation, connection awareness, and recovery for unsaved query tabs.

## Install

1. Download or build `dist/SsmsSqlHelper-2.0.0-beta.zip` and extract it.
2. Close every SSMS window.
3. Run `Install.cmd` from the extracted folder, then start SSMS.

Run `Install.cmd` again to update an existing installation. Run `Uninstall.cmd` to remove the extension. Installation is per user and does not require administrator rights. Your snippets and settings remain on disk after uninstalling.

For a local development build, close SSMS and run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dev-install.ps1
```

## Quick reference

| Action | How to use it |
| --- | --- |
| Expand a snippet | Type a shortcut such as `ssf`, `ii`, or `uu`, then press **Tab**. |
| Expand `SELECT *` | Put the caret on `*` in `SELECT * FROM dbo.TableName`, then press **Tab** to choose columns. |
| Add procedure parameters | Type `EXEC dbo.ProcedureName`, then press **Tab**. |
| Open an object for editing | Press **F12** on a procedure, view, or function to open an `ALTER` script in a new query tab. On a table, F12 opens Table Designer. |
| Find an object | Press **Ctrl+F12** on a procedure, table, view, or function to select it in Object Explorer. |
| Open suggestions | Press **Ctrl+Space** or **Ctrl+J**. |

F12 and Ctrl+F12 also work on a procedure name written alone, without `EXEC`. An `ALTER` script opened with F12 is **never executed automatically**.

## Writing SQL

### Completion

- Suggests tables after `FROM`, `JOIN`, `INSERT INTO`, and `UPDATE`.
- Suggests columns in `SELECT`, `WHERE`, `ON`, and `ORDER BY`, and after `alias.`.
- Suggests SQL clauses and operators for the current position. Typing `WHERE `, `AND `, `OR `, or `ON ` switches to condition suggestions such as `EXISTS` without requiring Tab.
- Suggests functions such as `GETDATE()`, `ISNULL(`, `COALESCE(`, and `IIF(` in both top-level and nested expressions. Functions and columns can appear in the same list.
- Shows function syntax and parameter hints while typing, for example `CAST(expression AS data_type)`.
- Colors matched `()`, `{}`, and `[]` by nesting level, and outlines the pair at the caret in red. Brackets in strings and comments are ignored.

### Snippets and Tab expansions

Type `ssf` and press Tab to insert `SELECT * FROM `, including inside `WHERE EXISTS (...)`. A complete snippet shortcut takes priority even when a column completion list is open. The table list appears after the expansion.

Tab can also generate an `INSERT` column list, add named arguments to an `EXEC` call, and expand a `JOIN` condition. Generated `NULL` argument values are placeholders to edit before running the query. Table aliases are generated automatically where useful; for example, `BudgetLines` becomes `bl`.

Snippet templates support tab stops (`$1`, `${1:text}`, `$0`), `$CURSOR$`, `$SELECTED$`, and variables such as `$DATE$`, `$USER$`, `$SERVER$`, `$DATABASE$`, and `$CLIPBOARD$`. Use `$$` for a literal dollar sign.

## Connection safety and recovery

- A banner above each query shows its server and database. New installations include the color rule `*prod* = #FF6363`. Edit the rule without changing previously saved settings.
- The extension warns before executing an `UPDATE` or `DELETE` statement without `WHERE`.
- Unsaved query tabs are copied to disk after three seconds without typing. After an unexpected SSMS shutdown, use **Tools → SQL Helper → Recover Unsaved Queries**.
- Metadata is cached separately by server, database, and user, and refreshed when the schema changes.

Backup files are stored in `%AppData%\SsmsSqlHelper\Backups\<session>\`. Normal saves and tab closes remove their backup. Recovered files are stored in `%AppData%\SsmsSqlHelper\Recovered\` and open without a database connection. Old backups are removed after 14 days. **Backed-up SQL is not encrypted.**

## Settings and updates

Open **Tools → SQL Helper → Edit Snippets** to manage snippets, feature switches, and connection banner colors. Under **Editor highlighting**, control nesting colors and the red matching-bracket border separately. Changes apply to open query tabs after you save. Other commands in that menu include Show Active Connection, Surround With Snippet, Recover Unsaved Queries, and Refresh Metadata.

| File | Purpose |
| --- | --- |
| `%AppData%\SsmsSqlHelper\snippets.json` | Your snippets |
| `%AppData%\SsmsSqlHelper\settings.json` | Feature switches and connection colors |
| `src/SsmsSqlHelper/Snippets/DefaultSnippets.json` | Built-in snippet templates |

**Tools → SQL Helper → Check for Updates** checks GitHub Releases only when selected. If an update is available, choose **Yes** to open the [Releases page](https://github.com/jirakitc/SSMS-SQoL/releases) in your browser. The check requires an internet connection.

## Build from source

You need SSMS 22, .NET Framework 4.8, and Visual Studio 2026 or Build Tools with the *Visual Studio extension development* workload. The project references SSMS assemblies from the build machine; it does not bundle them in the VSIX.

```powershell
msbuild SsmsSqlHelper.sln /restore
dotnet test tests\SsmsSqlHelper.Tests
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

The Debug VSIX is written to `src\SsmsSqlHelper\bin\Debug\SsmsSqlHelper.vsix`. The release script runs tests, builds the Release VSIX, and creates `dist\SsmsSqlHelper-<version>.zip`.

The VSIX version is in `src/SsmsSqlHelper/source.extension.vsixmanifest`; the installer label comes from `AssemblyInformationalVersion` in `Properties/AssemblyInfo.cs` (currently `2.0.0-beta`). VSIX requires a numeric version, so `AssemblyVersion` and `AssemblyFileVersion` must match it. **Change version numbers only when explicitly requested.**

| Directory | Responsibility |
| --- | --- |
| `src/SsmsSqlHelper/Editor/` | Completion, shortcuts, and editor UI |
| `src/SsmsSqlHelper/Parsing/`, `Generation/` | T-SQL context and generated SQL |
| `src/SsmsSqlHelper/Metadata/`, `Ssms/` | Database metadata and SSMS integration |
| `src/SsmsSqlHelper/Snippets/`, `Settings/`, `Backup/` | Snippets, preferences, and query recovery |
| `tests/SsmsSqlHelper.Tests/` | Logic tests that run without SSMS |

## Troubleshooting and limitations

If the extension does not appear, close SSMS and run:

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
```

Check **View → Output → SQL Helper** or `%LocalAppData%\SsmsSqlHelper\log.txt`. For package loading errors, search for `SsmsSqlHelper` in `%AppData%\Microsoft\SSMS\22.0_*\ActivityLog.xml`.

- Only SSMS 22 is supported.
- F12 requires a readable T-SQL definition for views and functions. Encrypted objects, or objects whose definition you cannot access, cannot be opened as an `ALTER` script.
