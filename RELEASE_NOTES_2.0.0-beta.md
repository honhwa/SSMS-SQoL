# SQL Helper 2.0.0-beta

This beta release expands SQL editing and object navigation in **SQL Server Management Studio 22**.

## Highlights

- **Navigate and edit database objects:** Press **Ctrl+F12** on a procedure, table, view, or function to select it in Object Explorer. Press **F12** to open a procedure, view, or function as an `ALTER` script in a new query tab, or to open Table Designer for a table. Procedure names also work without an `EXEC` prefix. Scripts opened with F12 are not executed automatically.
- **Complete stored procedure calls:** Type `EXEC dbo.ProcedureName` and press **Tab** to generate named parameters with type hints.
- **Write expressions faster:** Function suggestions now include `IIF` and appear alongside column names, including inside nested function calls. Parameter hints show syntax such as `CAST(expression AS data_type)` while typing.
- **See nested brackets clearly:** Matched `()`, `{}`, and `[]` use colors based on nesting depth. Brackets inside strings and comments are ignored.
- **Check for updates:** Use **Tools → SQL Helper → Check for Updates** to check GitHub Releases and open the Releases page.

## Completion and editor fixes

- Suggestions switch to table names after `FROM `, and to condition keywords such as `EXISTS` after manually typing `WHERE `, `AND `, `OR `, or `ON `; pressing Tab on the keyword is no longer required.
- A complete snippet shortcut such as `ssf` expands with **Tab** even when a column suggestion list is open, including inside `WHERE EXISTS (...)`.
- Rapid deletion no longer triggers the SSMS “Value does not fall within the expected range” dialog observed with completion open.
- Object Explorer navigation handles the active server and database connection more reliably.
- Snippet Manager settings are grouped for easier scanning. New installations start with the connection banner color rule `*prod* = #FF6363`; existing saved rules are preserved.

## Install or update

1. Download `SsmsSqlHelper-2.0.0-beta.zip` and extract it.
2. Save your queries and close all SSMS windows.
3. Run `Install.cmd`, then reopen SSMS.

Installation is per user. Running `Install.cmd` again updates an existing installation without removing your snippets or settings. `Uninstall.cmd` removes the extension.

## Notes

- Supports **SSMS 22** only.
- F12 can open an `ALTER` script only when the object has a readable T-SQL definition and your account has permission to view it.
- Unsaved query backups are stored in the Windows user profile as plain text.
- Release build and **523 automated tests** passed for this version.
