# PdbLens

PdbLens is a WinUI 3 PDB symbol explorer powered by Microsoft DIA and
[SymWrap](https://github.com/0cch/SymWrap).

## Features

- Open one or more PDB files.
- Browse structs, classes, unions, enums, typedefs, functions, data, and public symbols.
- Search and filter symbols without loading every symbol name into managed memory.
- Render the first level of UDT definitions in C/C++ syntax.
- Click referenced child types to jump to the target definition.
- Automatically select and scroll the target symbol in the left list.
- Navigate backward and forward between symbol definitions.
- Copy the current C/C++ definition with one click.
- Load PDBs from the command line or by drag and drop.

## Requirements

- Windows 10 1809 or newer, x64.
- Visual Studio 2022 with the Desktop development with C++ and DIA SDK components.
- .NET 9 SDK.
- Windows App SDK runtime, or the self-contained runtime packaged by the build.
- WebView2 Runtime.

`msdia140.dll` is copied automatically from the installed DIA SDK by the native
build and deployment steps.

## Build

```powershell
.\build.ps1 -Configuration Debug
```

The script builds the native DIA bridge, builds the WinUI 3 application, and runs
both native and managed tests.

Create an unsigned MSIX package with:

```powershell
.\build.ps1 -Configuration Release -Package
```

The application is produced under:

```text
src/PdbLens.App/bin/x64/Debug/net9.0-windows10.0.26100.0/win-x64/
```

Run it with one or more PDB paths:

```powershell
.\src\PdbLens.App\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\PdbLens.App.exe .\sample.pdb
```

## Architecture

```text
PdbLens.App       WinUI 3 UI, WebView2 code rendering, navigation
PdbLens.Core      P/Invoke contract, dedicated DIA worker thread, paging models
PdbLens.Native    C ABI bridge, compact symbol index, DIA definition builder
SymWrap           DIA lifecycle and symbol rendering primitives (submodule)
```

All DIA calls run on one dedicated STA worker thread. The UI never accesses DIA
objects directly.

### Memory behavior

- Native symbol indexes store only `SymIndexId` and category metadata.
- Symbol names are loaded page by page.
- The left list retains at most 1,600 rows.
- Only the selected definition is retained in the UI.
- Definitions are generated on demand; no PDB-wide definition cache is built.
- Unloading a PDB releases its DIA session and symbol index.

### Symbol navigation

The native layer resolves direct member types through DIA type relationships and
returns link ranges as UTF-16 character offsets. WebView2 renders those ranges as
clickable anchors and posts navigation requests back to WinUI.

This avoids parsing rendered C/C++ text with regular expressions.

## Third-party components

- Microsoft Windows App SDK
- Microsoft WebView2
- Microsoft DIA SDK / `msdia140.dll`
- SymWrap, LGPL-3.0

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

PdbLens is licensed under LGPL-3.0. See [LICENSE](LICENSE).
