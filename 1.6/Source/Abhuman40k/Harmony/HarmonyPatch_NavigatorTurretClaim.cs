using HarmonyLib;
using RimWorld;
using Verse;

namespace Abhuman40k;

/// <summary>
/// Stops the player from claiming, uninstalling or deconstructing the downed gravship's turrets,
/// which vanilla allows as soon as the wreck's grid is cut and the turrets lose power.
/// </summary>
[HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.ClaimableBy))]
public class NavigatorTurretClaimPatch
{
    public static void Postfix(Building_TurretGun __instance, Faction by, ref AcceptanceReport __result)
    {
        if (!__result.Accepted || by != Faction.OfPlayer || !__instance.Spawned)
        {
            return;
        }

        var rescueComp = __instance.Map.GetComponent<MapComponent_NavigatorRescue>();
        if (rescueComp == null || !rescueComp.IsShipTurret(__instance))
        {
            return;
        }

        __result = "BEWH.Abhuman.Reactor.TurretMachineSpiritRefuses".Translate();
    }
}
