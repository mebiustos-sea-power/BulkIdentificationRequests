# Bulk Identification Requests

Version **0.1.0**. Plugin ID: `mebiustos.bulkidentificationrequests`. Package folder: `BulkIdentificationRequests`.

Adds **Request identification from all unknown ships** immediately below **Orders > Identify** in a player unit's right-click menu. With Japanese selected, the label is **全不明艦の身元を求める**. Other game languages use English for the added label and tooltip. The same unit menu is used by the map and formation manager.

## Installation and use

1. Install Anchor Chain and its required BepInEx preloader.
2. Close the game. Extract the release ZIP and copy the enclosed `BulkIdentificationRequests` folder into `Sea Power_Data/StreamingAssets`. If building from source, use `dist/BulkIdentificationRequests`.
3. Enable **Anchor Chain** and **Bulk Identification Requests** in the game's mod manager, then restart the game.
4. Right-click the player-controlled unit that should transmit, open **Orders**, and select the new command. The right-clicked unit transmits even when another unit is selected.

Each activation takes a snapshot of the player's known map contacts and attempts the standard identification request once for each eligible contact. Eligible contacts are displayed as unknown surface contacts and are not already identified. Contacts outside that snapshot, aircraft, underwater contacts, weapons, and contacts with known allegiance are excluded. Duplicate track IDs are processed once. Eligibility is checked again immediately before each request.

The mod invokes the game's existing identification callback. Line of sight, radio horizon, range, submerged-state restrictions, responses, refusal, nonresponse, event-log messages, and exposure of the transmitter all remain under the game's control. There are no automatic retries, automatic requests for new contacts, or forced disclosures. A successful request is not guaranteed. The command is disabled for a submerged sender. Destroyed senders cannot run it.

The menu command changes neither the player's selection nor the unit's movement orders. A temporary sender override applies only to the current identification request and is restored even if execution fails.

Disable the mod in the mod manager and restart to remove its patches. To uninstall, close the game and remove its package folder. No original game DLL or UI asset is modified.

## Building and checks

Use the Windows .NET SDK. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set the game and Anchor Chain directories. No NuGet downloads are required.

```powershell
.\build.ps1 -Test
.\build.ps1 -Test -InstallLocal
```

The build produces `dist/BulkIdentificationRequests`. Local installation requires the game to be closed. Tests use the installed game's assemblies and its bundled Mono runtime in a separate process, without attaching to the running game.

Automated checks cover target filtering, deduplication and snapshots, English/Japanese selection and English fallback, sender scoping/restoration, and menu ordering. A fixture executes the installed game's actual communication and identification-request IL with managed engine boundaries, including line of sight, radio horizon, submerged senders/targets, refusal, nonresponse, and disclosure. It does not copy those game algorithms.

The separate game-Mono process checks callback discovery/binding and method compatibility against the installed game. It installs the production sender transpiler through Harmony on a managed sender boundary and verifies dispatch, normal manual behavior, selection preservation, and patch removal. It does not detour the real game methods in that process: their Unity-dependent initializers require the engine. Actual game-method patch installation, menu rendering, and battle effects remain in-game verification items.

## Implementation

- `MenuExtension`: adds the command to the existing unit menu.
- `ContactSelection` / `BulkRequests`: select only player-visible unknown surface contacts and execute a single batch.
- `VanillaRequests`: discovers the standard callback from the existing menu's IL and invokes it. A narrowly scoped Harmony transpiler changes only its communication sender lookup during a bulk request.
- `RequestSource`: restores the previous request context on success and failure without touching global selection.
- `Plugin` / `ModActivation`: respect the game's package checkbox and roll back this mod's patches on initialization failure.

Unsupported game-method layouts disable bulk requests and log a compatibility error. Runtime failures are logged and stop the batch; they are not treated as normal radio refusal or nonresponse. Logs use the **Bulk Identification Requests** BepInEx log source.
