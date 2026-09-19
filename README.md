# No Magic Fridges

An [Oxygen Not Included](https://www.klei.com/games/oxygen-not-included) mod that makes a powered fridge **only ever cool** its contents. Food already colder than the fridge's 1 °C target is left alone instead of being warmed up to it.

## The problem

A powered Refrigerator or Mini Fridge does not set its contents to 1 °C; it attaches a simulated heat reservoir at 1 °C (400 kJ/K, 1000 W/K) to every stored item. That exchange runs both ways, so food you deep-froze by keeping the fridge in a cold room is dragged back up to 1 °C the moment the fridge gets power, even for a few seconds. Unpowered, the food just exchanges heat with the room as normal.

## What the mod does

- While a fridge is powered, the 1 °C reservoir is attached only to items **warmer** than 1 °C. Items at or below it keep exchanging heat with the room exactly as they would unpowered.
- Once a second the fridge re-checks its contents: an item the room has warmed back above 1 °C gets the reservoir again, and an item that has reached 1 °C lets go of it. The fridge is a ceiling on food temperature, not a magnet to it.
- Applies to the Refrigerator, the Mini Fridge, and any modded building that reuses the vanilla fridge controller.
- Nothing else changes: power draw, cooling and energy-saver states, storage, the full signal (which still requires power, as in vanilla), and spoilage rules are all untouched. Safe to add to or remove from an existing save.

## Building

Requires the .NET SDK (8+). Shared build configuration lives in the [oni-mods-common](https://github.com/isochronous/oni-mods-common) submodule, so clone with `--recurse-submodules` (or run `git submodule update --init`). The game DLLs are referenced directly from the game install; override the path if yours differs:

```
dotnet build src/NoMagicFridges -c Release -p:GameFolder="<path-to>\OxygenNotIncluded"
```

A successful build deploys the mod to `Documents\Klei\OxygenNotIncluded\mods\local\NoMagicFridges` (disable with `-p:ModDeployFolder=none`). Harmony only, no PLib.

## Implementation notes

- `SimulatedTemperatureAdjuster.OnItemSimRegistered` (vanilla) attaches the reservoir whenever an item is registered: fridge powers up, item delivered, item spawned. A Harmony prefix skips the attachment, sending a zeroed adjuster instead, for items at or below the target. It only acts when the adjuster's storage carries the mod's `FridgeThermostat`, so other users of the class are untouched.
- `FridgeThermostat` (added to every prefab with a `RefrigeratorController.Def` after buildings are generated) re-evaluates each stored item every second while the fridge is active and sends `SimMessages.ModifyElementChunkTemperatureAdjuster` only when an item's state changes.
