using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Abhuman40k;

public class GenStep_DownedNavigator : GenStep_Scatterer
{
    private const float HullRadius = 45f;
    private static readonly FloatRange HullHitPointsRange = new(0.35f, 0.65f);

    public override int SeedPart => 931842770;

    protected override bool CanScatterAt(IntVec3 c, Map map)
    {
        if (base.CanScatterAt(c, map) && c.Standable(map))
        {
            return !c.Fogged(map);
        }
        return false;
    }

    protected override void ScatterAt(IntVec3 loc, Map map, GenStepParams parms, int count = 1)
    {
        Pawn pawn;
        if (parms.sitePart is { things.Any: true })
        {
            pawn = (Pawn)parms.sitePart.things.Take(parms.sitePart.things[0]);
        }
        else
        {
            var component = map.Parent.GetComponent<DownedRefugeeComp>();
            pawn = component == null || !component.pawn.Any ? DownedRefugeeQuestUtility.GenerateRefugee(map.Tile) : component.pawn.Take(component.pawn[0]);
        }
        pawn.genes?.SetXenotype(Abhuman40kDefOf.BEWH_Navigator);
        HealthUtility.DamageUntilDowned(pawn, allowBleedingWounds: false);
        HealthUtility.DamageLegsUntilIncapableOfMoving(pawn, allowBleedingWounds: false);
        var casket = map.listerBuildings.AllBuildingsNonColonistOfDef(ThingDefOf.DeathrestCasket).FirstOrDefault();
        if (casket != null)
        {
            loc = casket.Position;
        }
        else
        {
            Log.Warning("[RimDark Abhumans] No deathrest casket in the downed gravship layout; the navigator was scattered instead.");
        }
        GenSpawn.Spawn(pawn, loc, map);
        pawn.mindState.WillJoinColonyIfRescued = true;
        MapGenerator.SetVar("RectOfInterest", CellRect.CenteredOn(loc, 1, 1));

        var rescueComp = map.GetComponent<MapComponent_NavigatorRescue>();
        rescueComp?.Register(pawn);

        SpawnReactor(map);
        SpawnTurrets(map, rescueComp);
        DisarmTurrets(map, rescueComp);
        DrainPodLaunchers(map);
        BatterHull(map, RegisterShipSystems(map, rescueComp));
    }

    /// <summary>
    /// Registers the wreck's generators and batteries, whose destruction sets off the reactor.
    /// Returns those plus the turrets, as the parts that mark out the hull.
    /// </summary>
    private static List<Thing> RegisterShipSystems(Map map, MapComponent_NavigatorRescue rescueComp)
    {
        var parts = new List<Thing>();
        foreach (var building in map.listerBuildings.allBuildingsNonColonist)
        {
            if (building is Building_TurretGun)
            {
                parts.Add(building);
                continue;
            }

            if (building.TryGetComp<CompPowerPlant>() == null && building.TryGetComp<CompPowerBattery>() == null)
            {
                continue;
            }

            parts.Add(building);
            rescueComp?.RegisterShipSystem(building);
        }

        return parts;
    }

    /// <summary>
    /// Leaves the wreck's walls at reduced hit points so explosions inside it breach the hull.
    /// </summary>
    private static void BatterHull(Map map, List<Thing> shipParts)
    {
        if (shipParts.Count == 0)
        {
            return;
        }

        int x = 0, z = 0;
        foreach (var part in shipParts)
        {
            x += part.Position.x;
            z += part.Position.z;
        }

        var center = new IntVec3(x / shipParts.Count, 0, z / shipParts.Count);

        foreach (var building in map.listerBuildings.allBuildingsNonColonist)
        {
            if (!building.def.IsWall || !building.def.useHitPoints || building.def.building.isNaturalRock)
            {
                continue;
            }

            if (!building.Position.InHorDistOf(center, HullRadius))
            {
                continue;
            }

            building.HitPoints = Mathf.Max(1, Mathf.RoundToInt(building.MaxHitPoints * HullHitPointsRange.RandomInRange));
        }
    }

    private static void SpawnReactor(Map map)
    {
        var markers = TakeMarkers(map, Abhuman40kDefOf.PollutionPump);
        if (markers.Count == 0)
        {
            Log.Warning("[RimDark Abhumans] No " + Abhuman40kDefOf.PollutionPump.defName + " reactor marker found in the downed gravship layout; no critical reactor was spawned.");
            return;
        }

        GenSpawn.Spawn(Abhuman40kDefOf.BEWH_NavigatorCriticalReactor, markers[0], map);
    }

    private static void SpawnTurrets(Map map, MapComponent_NavigatorRescue rescueComp)
    {
        var markers = TakeMarkers(map, Abhuman40kDefOf.SchoolDesk);
        if (markers.Count == 0)
        {
            Log.Warning("[RimDark Abhumans] No " + Abhuman40kDefOf.SchoolDesk.defName + " turret markers found in the downed gravship layout; no turrets were spawned.");
            return;
        }

        foreach (var spawnLoc in markers)
        {
            var turret = ThingMaker.MakeThing(ThingDefOf.Turret_MiniTurret, ThingDefOf.Steel);
            GenSpawn.Spawn(turret, spawnLoc, map);
            rescueComp?.RegisterTurret(turret);
        }
    }

    /// <summary>
    /// Removes every marker building of the given def and returns the cells they stood on.
    /// KCSG cannot place modded defs, so the layout marks these positions with vanilla stand-ins.
    /// </summary>
    private static List<IntVec3> TakeMarkers(Map map, ThingDef markerDef)
    {
        var markers = map.listerBuildings.AllBuildingsNonColonistOfDef(markerDef).ToList();
        var positions = markers.Select(marker => marker.Position).ToList();

        foreach (var marker in markers)
        {
            marker.Destroy();
        }

        return positions;
    }

    private static void DisarmTurrets(Map map, MapComponent_NavigatorRescue rescueComp)
    {
        foreach (var turret in map.listerBuildings.allBuildingsNonColonist.OfType<Building_TurretGun>().ToList())
        {
            if (turret.Faction != null)
            {
                turret.SetFaction(null);
            }

            rescueComp?.RegisterTurret(turret);
        }
    }

    private static void DrainPodLaunchers(Map map)
    {
        foreach (var building in map.listerBuildings.allBuildingsNonColonist.ToList())
        {
            if (building.def != Abhuman40kDefOf.PodLauncher && building.TryGetComp<CompLaunchable>() == null)
            {
                continue;
            }

            var refuelable = building.TryGetComp<CompRefuelable>();
            if (refuelable != null && refuelable.Fuel > 0f)
            {
                refuelable.ConsumeFuel(refuelable.Fuel);
            }
        }
    }
}
