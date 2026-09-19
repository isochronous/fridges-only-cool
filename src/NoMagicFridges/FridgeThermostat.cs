#pragma warning disable 649, 169 // [MyCmpGet] fields are populated by the game via reflection
using System.Collections.Generic;
using UnityEngine;

namespace NoMagicFridges
{
	/// <summary>
	/// Keeps a powered fridge's simulated 1 °C reservoir attached only to items that are
	/// warmer than the target. The vanilla SimulatedTemperatureAdjuster attaches it to every
	/// stored item whenever the fridge is active, which warms deep-frozen food up to 1 °C.
	///
	/// The Harmony prefix in Patches handles the moment an item is (re)registered; this
	/// component re-evaluates every second so an item the room warms back above the target
	/// gets the reservoir again, and one that has reached the target lets go of it.
	/// </summary>
	public sealed class FridgeThermostat : KMonoBehaviour, ISim1000ms
	{
		[MyCmpGet] private Storage storage;
		[MyCmpGet] private Operational operational;

		private RefrigeratorController.Def def;
		/// <summary>Last adjuster state sent per item (instance id), so the sim is only messaged on change.</summary>
		private readonly Dictionary<int, bool> attached = new Dictionary<int, bool>();
		private readonly List<int> stale = new List<int>();
		private readonly HashSet<int> seen = new HashSet<int>();

		/// <summary>True when the item should have the fridge's reservoir attached.</summary>
		public static bool WantsCooling(PrimaryElement element, float target)
		{
			return element != null && element.Temperature > target;
		}

		protected override void OnSpawn()
		{
			base.OnSpawn();
			def = gameObject.GetDef<RefrigeratorController.Def>();
		}

		public void Sim1000ms(float dt)
		{
			if (def == null || storage == null || operational == null)
				return;
			if (!operational.IsActive)
			{
				// Vanilla detaches everything when the fridge stops; forget our bookkeeping too.
				attached.Clear();
				return;
			}
			seen.Clear();
			foreach (GameObject item in storage.items)
			{
				if (item == null)
					continue;
				SimTemperatureTransfer transfer = item.GetComponent<SimTemperatureTransfer>();
				if (transfer == null || !Sim.IsValidHandle(transfer.SimHandle))
					continue;
				int id = item.GetInstanceID();
				seen.Add(id);
				bool wanted = WantsCooling(item.GetComponent<PrimaryElement>(), def.simulatedInternalTemperature);
				if (attached.TryGetValue(id, out bool current) && current == wanted)
					continue;
				attached[id] = wanted;
				if (wanted)
					SimMessages.ModifyElementChunkTemperatureAdjuster(transfer.SimHandle, def.simulatedInternalTemperature, def.simulatedInternalHeatCapacity, def.simulatedThermalConductivity);
				else
					SimMessages.ModifyElementChunkTemperatureAdjuster(transfer.SimHandle, 0f, 0f, 0f);
			}
			stale.Clear();
			foreach (int id in attached.Keys)
				if (!seen.Contains(id))
					stale.Add(id);
			foreach (int id in stale)
				attached.Remove(id);
		}

		/// <summary>Called by the registration prefix so the periodic pass agrees with what vanilla just did.</summary>
		public void NoteRegistered(GameObject item, bool wanted)
		{
			if (item != null)
				attached[item.GetInstanceID()] = wanted;
		}
	}
}
