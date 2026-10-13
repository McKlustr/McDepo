# Domain Appointer Pro

ArcGIS Pro add-in that recreates the Domain Appointer workflow from the ArcMap RS_Tools add-in (Woolpert / Chris Stayte).

## What it does

1. Click **Domain Appointer** (Add-In tab) to open the configuration dockpane.
2. Select a feature layer that has a coded-value domain field.
3. Select the field.
4. Map any of the domain codes to the fixed slots **00–19** and **Null**.
5. Click **Save Configuration**.
6. Select features on the map and click the corresponding Domain button (or use a keyboard shortcut) to write the mapped code into the field.

Settings are stored in `%AppData%\DomainAppointerPro\settings.json` and survive Pro restarts.

Buttons for unmapped slots stay disabled. Null is always available once a layer/field is configured: if you map a domain code to Null, that code is written; if Null is left unmapped, the field is cleared (database null). Use a mapped Null code for non-nullable fields that have an N/A / Unknown domain value.

To unmap a slot, choose **(unmapped)** in the dropdown or click **Clear**, then save.

## Requirements

- ArcGIS Pro 3.0 or later
- Visual Studio 2022 (or later) with the ArcGIS Pro SDK for .NET installed
  - https://github.com/Esri/arcgis-pro-sdk/wiki#installing-arcgis-pro-sdk-for-net

## Build & Install

1. Open `DomainAppointerPro.csproj` in Visual Studio.
2. Confirm the `ProInstallDir` property points to your Pro installation (default `C:\Program Files\ArcGIS\Pro\`).
   - `ArcGIS.Desktop.Framework.dll` is under `bin\`, not `bin\Extensions\Core\`. Core / Mapping / Editing stay under `bin\Extensions\<name>\`.
3. Build **x64** (Debug or Release). Do not use Any CPU — Pro assemblies are AMD64. If VS still shows Any CPU, Build → Configuration Manager → set platform to x64.
4. If the Esri targets are present, the build produces a `.esriAddInX` package. Otherwise double-click the generated add-in after packaging, or copy the output folder into a well-known add-in folder.
5. In ArcGIS Pro: Project → Add-In Manager → Install the add-in, then restart Pro.

## Keyboard Shortcuts

After the add-in is loaded:

1. Open the Keyboard Shortcuts dialog (F12 or Help → Shortcuts).
2. Search for “Domain” or the individual button captions (00, 01, … Null).
3. Assign Ctrl+1, Ctrl+2, etc. (or any combination you prefer).

The shortcuts become available whenever a map view is active.

## Project structure

```
DomainAppointerPro/
  Config.daml                          UI definition + button registration
  Module1.cs                           Module entry point (loads settings)
  DomainAppointer/
    DomainSettings.cs                  Persistent mapping storage (%AppData%)
    DomainLookup.cs                    Coded-value domains including per-subtype assignments
    ApplyDomainService.cs              Core logic that writes the attribute via EditOperation
    ApplyDomainButton.cs               Shared button for slots 00-19 / Null (enablement + click)
    LauncherButton.cs                  Opens the config dockpane
    ConfigDockpaneViewModel.cs         Configuration UI logic
    ConfigDockpaneView.xaml(+.cs)      Configuration UI
  Images/DomainAppointer/              Icons (copied from original RS_Tools)
```

## Notes / limitations

- Only coded-value domains are supported (range domains are ignored). Fields whose domain is assigned per subtype are included; codes from the default domain and every subtype domain are unioned in the mapping list.
- The layer must be present and selectable in the active map.
- Edits participate in the Pro edit session / undo stack via `EditOperation` + `Inspector`.
- If the field type is numeric, the code string is converted automatically when possible.
- Layer is resolved first by URI (stable across renames), then by name.
