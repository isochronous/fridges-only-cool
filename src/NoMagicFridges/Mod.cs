using HarmonyLib;
using KMod;

namespace NoMagicFridges
{
	public sealed class NoMagicFridgesMod : UserMod2
	{
		public override void OnLoad(Harmony harmony)
		{
			base.OnLoad(harmony);
			Debug.Log("[NoMagicFridges] Loaded version " + typeof(NoMagicFridgesMod).Assembly.GetName().Version);
		}
	}
}
