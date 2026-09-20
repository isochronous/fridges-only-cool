using HarmonyLib;
using KMod;

namespace FridgesOnlyCool
{
	public sealed class FridgesOnlyCoolMod : UserMod2
	{
		public override void OnLoad(Harmony harmony)
		{
			base.OnLoad(harmony);
			Debug.Log("[FridgesOnlyCool] Loaded version " + typeof(FridgesOnlyCoolMod).Assembly.GetName().Version);
		}
	}
}
