# No Magic Fridges

An [Oxygen Not Included](https://www.klei.com/games/oxygen-not-included) mod that makes a powered fridge **only ever cool** its contents. Food already colder than the fridge's 1 °C target is insulated from the room instead of being warmed up to it.

## The problem

A powered Refrigerator or Mini Fridge does not set its contents to 1 °C; it attaches a simulated heat reservoir at 1 °C (400 kJ/K, 1000 W/K) to every stored item. That exchange runs both ways, so food you deep-froze by keeping the fridge in a cold room is dragged back up to 1 °C the moment the fridge gets power, even for a few seconds. Unpowered, the food just exchanges heat with the room as normal.

## What the mod does

While a fridge is powered, each stored item is treated one of two ways:

- **Warmer than 1 °C:** the vanilla reservoir is attached and the item is cooled to 1 °C, exactly as before.
- **At or below 1 °C:** no reservoir. Instead the item's heat exchange with the room is scaled down by the same factor an Insulated Tile uses (1/100), so it still drifts toward room temperature, just about a hundred times slower. This is insulation in the Insulated Tile sense, not the Insulite sense: the exchange is slowed, not stopped.

Once a second the fridge re-checks its contents and moves items between the two states. An insulated item the room eventually warms above 1 °C gets the reservoir and is cooled back down; a cooled item that reaches 1 °C becomes insulated. Unpowered fridges and items taken out of a fridge get their normal heat exchange back immediately.

- Applies to the Refrigerator, the Mini Fridge, and any modded building that reuses the vanilla fridge controller.
- Nothing else changes: power draw, cooling and energy-saver states, storage, the full signal (which still requires power, as in vanilla), and spoilage rules are all untouched. The insulation is not saved with the game; it is re-applied on load. Safe to add to or remove from an existing save.

## Building

Requires the .NET SDK (8+). Shared build configuration lives in the [oni-mods-common](https://github.com/isochronous/oni-mods-common) submodule, so clone with `--recurse-submodules` (or run `git submodule update --init`). The game DLLs are referenced directly from the game install; override the path if yours differs:

```
dotnet build src/NoMagicFridges -c Release -p:GameFolder="<path-to>\OxygenNotIncluded"
```

A successful build deploys the mod to `Documents\Klei\OxygenNotIncluded\mods\local\NoMagicFridges` (disable with `-p:ModDeployFolder=none`). Harmony only, no PLib.

## Implementation notes

- `SimulatedTemperatureAdjuster.OnItemSimRegistered` (vanilla) attaches the reservoir whenever an item is registered with the sim: fridge powers up, item delivered, item spawned, chunk re-registered. A Harmony prefix sends a zeroed adjuster instead for items at or below the target. It only acts when the adjuster's storage carries the mod's `FridgeThermostat`, so other users of the class are untouched.
- `FridgeThermostat` (added to every prefab with a `RefrigeratorController.Def` after buildings are generated) evaluates each stored item every second. Insulation is applied by scaling the item's `SimTemperatureTransfer` surface area and ground-transfer scale and re-registering its sim chunk (the component's own `SimUnregister`/`SimRegister`, which sync the temperature across the swap). Originals are restored, with another re-registration, when the item warms past the target, the fridge loses power, the item leaves the fridge, or the fridge is removed.
