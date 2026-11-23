# Sun Lens Flare Remover

Version **0.1.0** / Plugin ID: `mebiustos.sunlensflareremover`

An Anchor Chain mod for Sea Power that disables the sun's lens flare, including star-shaped rays and lens ghosts. It preserves the sun disc, sky lighting, sunset colors, sea reflections, and the moon's flare. No configuration file is required.

## Installation

1. Install Anchor Chain and its required BepInEx preloader.
2. Exit Sea Power and copy the `dist/SunLensFlareRemover` folder into `Sea Power_Data/StreamingAssets`.
3. Enable Anchor Chain and Sun Lens Flare Remover in the mod manager, then restart Sea Power.

To disable the mod, uncheck it in the mod manager and restart the game. To uninstall it, exit the game and delete the installed mod folder. No changes to the game's DLLs or environment.ini are required.

## Building

Use a .NET SDK on Windows. The mod targets netstandard2.1, and the verification program targets net10.0. No NuGet packages are downloaded.

Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set the paths to the game and the folder containing the AnchorChain.dll you use. You can also supply these paths as command-line arguments to the build script.

```powershell
.\build.ps1 -Test
```

The output is `dist/SunLensFlareRemover`. The build script does not install the mod or launch the game.

## Implementation and verification

A Harmony postfix on `DynamicSky.updateFlares` sets only the sun's Light.flare to null. The game restores the flare during sky updates, so the postfix removes it after each update. The source flare asset and the moon's flare handling are preserved. Patches are installed only when this mod is enabled in the game's mod manager. Initialization failures are logged and the mod's patches are removed. The log source is Sun Lens Flare Remover.

The verification program reads the installed game's actual updateFlares IL and the built mod's postfix IL, then executes them with managed substitutes for Unity and scene boundaries. It checks day, night, underwater, and high-altitude cloud conditions; repeated updates; preservation of flare assets and light values; and restoration of the flare when the removal callback is not applied. It also checks that the field injected by Harmony matches the callback parameter type.

This verification does not exercise patch installation or rendering inside Unity. Visual comparisons of the sun and sea reflections must be performed in the game.
