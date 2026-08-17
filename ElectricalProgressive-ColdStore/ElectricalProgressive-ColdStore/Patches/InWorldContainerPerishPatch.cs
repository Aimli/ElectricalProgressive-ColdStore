using System;
using HarmonyLib;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveColdStore.Patches;

[HarmonyPatch(typeof(InWorldContainer), nameof(InWorldContainer.GetPerishRate))]
public static class InWorldContainerPerishPatch
{
    [HarmonyPostfix]
    public static void Postfix(InWorldContainer __instance, ref float __result)
    {
        BlockPos? pos = __instance.Inventory?.Pos;
        if (pos == null) return;

        float best = __result;
        if (ColdStoreRuntime.ServerManager?.TryGetPerishRate(pos, out float serverRate) == true)
            best = Math.Min(best, serverRate);

        if (ColdStoreRuntime.ClientManager?.TryGetPerishRate(pos, out float clientRate) == true)
            best = Math.Min(best, clientRate);

        __result = best;
    }
}
