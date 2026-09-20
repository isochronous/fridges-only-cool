#pragma warning disable 649, 169 // [MyCmpGet] fields are populated by the game via reflection
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FridgesOnlyCool
{
	/// <summary>
	/// Per-item thermal policy for a powered fridge. Vanilla attaches a simulated 1 °C
	/// reservoir to every stored item whenever the fridge is active, which also warms
	/// deep-frozen food up to 1 °C. This component instead keeps each item in one of three
	/// states while the fridge is active:
	///
	///   Cooling   - warmer than the target: the vanilla reservoir is attached (unchanged).
	///   Insulated - at or below the target: no reservoir, and the item's heat exchange with
	///               the room is scaled down by the same factor an Insulated Tile uses, so it
	///               still drifts toward room temperature, just very slowly.
	///   Normal    - fridge unpowered, or item left the fridge: vanilla behaviour.
	///
	/// Insulation is applied by re-registering the item's sim chunk with a reduced surface
	/// area and ground-transfer scale. The Harmony prefix in Patches keeps the reservoir off
	/// insulated items whenever vanilla (re)registers them.
	/// </summary>
	public sealed class FridgeThermostat : KMonoBehaviour, ISim1000ms
	{
		/// <summary>Insulated Tile thermal conductivity relative to its material (InsulationTileConfig).</summary>
		public const float InsulationFactor = 0.01f;

		private enum ItemState { Normal, Cooling, Insulated }

		private sealed class Tracked
		{
			public ItemState state;
			public bool originalsSaved;
			public float surfaceArea;
			public float groundTransferScale;
		}

		private static readonly Action<SimTemperatureTransfer> SimRegister = AccessTools.MethodDelegate<Action<SimTemperatureTransfer>>(AccessTools.Method(typeof(SimTemperatureTransfer), "SimRegister"));
		private static readonly Action<SimTemperatureTransfer> SimUnregister = AccessTools.MethodDelegate<Action<SimTemperatureTransfer>>(AccessTools.Method(typeof(SimTemperatureTransfer), "SimUnregister"));

		[MyCmpGet] private Storage storage;
		[MyCmpGet] private Operational operational;

		private RefrigeratorController.Def def;
		private readonly Dictionary<int, Tracked> items = new Dictionary<int, Tracked>();
		private readonly HashSet<int> seen = new HashSet<int>();
		private readonly List<int> stale = new List<int>();

		private static readonly EventSystem.IntraObjectHandler<FridgeThermostat> OnStorageChangeDelegate =
			new EventSystem.IntraObjectHandler<FridgeThermostat>((component, data) => component.OnStorageChange(data));

		/// <summary>True when the item is warm enough to want the fridge's reservoir.</summary>
		public static bool WantsCooling(PrimaryElement element, float target)
		{
			return element != null && element.Temperature > target;
		}

		protected override void OnSpawn()
		{
			base.OnSpawn();
			def = gameObject.GetDef<RefrigeratorController.Def>();
			Subscribe((int)GameHashes.OnStorageChange, OnStorageChangeDelegate);
		}

		protected override void OnCleanUp()
		{
			Unsubscribe((int)GameHashes.OnStorageChange, OnStorageChangeDelegate);
			if (storage != null)
				foreach (GameObject item in storage.items)
					if (item != null && items.TryGetValue(item.GetInstanceID(), out Tracked tracked) && tracked.originalsSaved)
						RestoreInsulation(item.GetComponent<SimTemperatureTransfer>(), tracked);
			items.Clear();
			base.OnCleanUp();
		}

		public void Sim1000ms(float dt)
		{
			if (def == null || storage == null || operational == null)
				return;
			bool active = operational.IsActive;
			seen.Clear();
			foreach (GameObject item in storage.items)
			{
				if (item == null)
					continue;
				SimTemperatureTransfer transfer = item.GetComponent<SimTemperatureTransfer>();
				if (transfer == null)
					continue;
				int id = item.GetInstanceID();
				seen.Add(id);
				if (!items.TryGetValue(id, out Tracked tracked))
					items[id] = tracked = new Tracked();
				// A chunk mid-registration has no valid handle yet; wait for it.
				if (!Sim.IsValidHandle(transfer.SimHandle))
					continue;
				ItemState wanted = !active ? ItemState.Normal
					: WantsCooling(item.GetComponent<PrimaryElement>(), def.simulatedInternalTemperature) ? ItemState.Cooling
					: ItemState.Insulated;
				if (wanted != tracked.state)
					Apply(transfer, tracked, wanted);
			}
			stale.Clear();
			foreach (int id in items.Keys)
				if (!seen.Contains(id))
					stale.Add(id);
			foreach (int id in stale)
				items.Remove(id);
		}

		private void Apply(SimTemperatureTransfer transfer, Tracked tracked, ItemState wanted)
		{
			ItemState previous = tracked.state;
			tracked.state = wanted;
			switch (wanted)
			{
				case ItemState.Insulated:
					if (!tracked.originalsSaved)
					{
						tracked.surfaceArea = transfer.SurfaceArea;
						tracked.groundTransferScale = transfer.GroundTransferScale;
						tracked.originalsSaved = true;
					}
					transfer.SurfaceArea = tracked.surfaceArea * InsulationFactor;
					transfer.GroundTransferScale = tracked.groundTransferScale * InsulationFactor;
					// Re-registration fires vanilla's OnItemSimRegistered, where the prefix
					// keeps the reservoir off because the item is at or below the target.
					Reregister(transfer);
					break;
				case ItemState.Cooling:
					if (previous == ItemState.Insulated)
						RestoreInsulation(transfer, tracked); // re-registers; the prefix lets vanilla attach the reservoir
					else
						SimMessages.ModifyElementChunkTemperatureAdjuster(transfer.SimHandle, def.simulatedInternalTemperature, def.simulatedInternalHeatCapacity, def.simulatedThermalConductivity);
					break;
				default:
					if (previous == ItemState.Insulated)
						RestoreInsulation(transfer, tracked);
					else
						SimMessages.ModifyElementChunkTemperatureAdjuster(transfer.SimHandle, 0f, 0f, 0f);
					break;
			}
		}

		private static void RestoreInsulation(SimTemperatureTransfer transfer, Tracked tracked)
		{
			if (transfer == null || !tracked.originalsSaved)
				return;
			transfer.SurfaceArea = tracked.surfaceArea;
			transfer.GroundTransferScale = tracked.groundTransferScale;
			tracked.originalsSaved = false;
			Reregister(transfer);
		}

		/// <summary>Drops and re-adds the item's sim chunk so new surface/transfer values take effect. Unregister syncs the temperature first.</summary>
		private static void Reregister(SimTemperatureTransfer transfer)
		{
			if (transfer == null || !transfer.isSpawned)
				return;
			SimUnregister(transfer);
			SimRegister(transfer);
		}

		/// <summary>An item leaving the fridge must get its normal heat exchange back immediately.</summary>
		private void OnStorageChange(object data)
		{
			GameObject item = data as GameObject;
			if (item == null || !items.TryGetValue(item.GetInstanceID(), out Tracked tracked))
				return;
			if (storage != null && storage.items.Contains(item))
				return;
			if (tracked.originalsSaved)
				RestoreInsulation(item.GetComponent<SimTemperatureTransfer>(), tracked);
			items.Remove(item.GetInstanceID());
		}

		/// <summary>Called by the registration prefix so the periodic pass agrees with what vanilla just did.</summary>
		public void NoteRegistered(GameObject item, bool reservoirAttached)
		{
			if (item == null)
				return;
			int id = item.GetInstanceID();
			if (!items.TryGetValue(id, out Tracked tracked))
				items[id] = tracked = new Tracked();
			if (reservoirAttached)
				tracked.state = ItemState.Cooling;
			else if (tracked.state == ItemState.Cooling)
				tracked.state = ItemState.Normal;
		}
	}
}
