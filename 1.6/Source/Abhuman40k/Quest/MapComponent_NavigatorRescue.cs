using System.Collections.Generic;
using System.Linq;
using Core40k;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Abhuman40k;

public class MapComponent_NavigatorRescue : MapComponent
{
    private const int PollIntervalTicks = 60;

    private Pawn navigator;
    private float ambushPoints = -1f;
    private bool triggered;
    private bool turretsAwake;
    private List<Thing> shipTurrets = new();
    private bool lastSeenOnThisMap = true;

    public MapComponent_NavigatorRescue(Map map) : base(map)
    {
    }

    public void Register(Pawn pawn)
    {
        navigator = pawn;
    }

    public void SetAmbushPoints(float points)
    {
        ambushPoints = points;
    }

    /// <summary>
    /// Records a turret as belonging to the wreck, so the machine spirit only ever takes back
    /// its own guns and never anything the player built or claimed elsewhere on the map.
    /// </summary>
    public void RegisterTurret(Thing turret)
    {
        if (turret != null && !shipTurrets.Contains(turret))
        {
            shipTurrets.Add(turret);
        }
    }

    public bool IsShipTurret(Thing turret)
    {
        return turret != null && shipTurrets.Contains(turret);
    }

    /// <summary>
    /// Called when the player shoots one of the dormant ship turrets. The machine spirit
    /// wakes early, but the reactor countdown and the salvagers still wait for the rescue.
    /// </summary>
    public void Notify_TurretsProvoked()
    {
        WakeTurrets();
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();

        if (navigator == null || Find.TickManager.TicksGame % PollIntervalTicks != 0)
        {
            return;
        }

        if (navigator.Dead || navigator.Destroyed)
        {
            navigator = null;
            return;
        }

        if (!triggered && Rescued())
        {
            triggered = true;
            Building_CriticalReactor.DestabilizeAllOnMap(map);
            WakeTurrets();
            SendSalvagers();
        }

        lastSeenOnThisMap = navigator.MapHeld == map;

        if (Secured())
        {
            GameComponent_PersistentQuests.MarkCompleted(GameComponent_NavigatorQuest.NavigatorIncidentDefName);
            navigator = null;
        }
    }

    public override void MapRemoved()
    {
        base.MapRemoved();

        if (navigator == null)
        {
            return;
        }

        var leftWithThePlayer = !navigator.Dead && !navigator.Destroyed
                                                 && navigator.Faction == Faction.OfPlayer
                                                 && !lastSeenOnThisMap;

        if (Secured() || leftWithThePlayer)
        {
            GameComponent_PersistentQuests.MarkCompleted(GameComponent_NavigatorQuest.NavigatorIncidentDefName);
        }

        navigator = null;
    }

    private bool Rescued()
    {
        if (navigator.Faction == Faction.OfPlayer)
        {
            return true;
        }

        return navigator.CarriedBy?.Faction == Faction.OfPlayer;
    }

    private bool Secured()
    {
        if (navigator == null || navigator.Dead || navigator.Destroyed || navigator.Faction != Faction.OfPlayer)
        {
            return false;
        }

        if (navigator.MapHeld != null)
        {
            return navigator.MapHeld != map;
        }

        if (navigator.GetCaravan() != null)
        {
            return true;
        }

        var situation = Find.WorldPawns.GetSituation(navigator);
        return situation is WorldPawnSituation.CaravanMember or WorldPawnSituation.InTravelingTransportPod;
    }

    private void WakeTurrets()
    {
        if (turretsAwake)
        {
            return;
        }

        turretsAwake = true;
        var machineSpirit = Faction.OfMechanoids;

        foreach (var turret in shipTurrets.ToList())
        {
            if (turret is not Building building || turret.Destroyed || !turret.Spawned)
            {
                continue;
            }

            if (building.Faction != machineSpirit)
            {
                building.SetFaction(machineSpirit);
            }

            PowerUp(building);
        }

        // The wreck's grid comes back to life with them - cold generators and flat batteries
        // would otherwise leave the turrets unpowered and silent.
        foreach (var building in AllBuildings())
        {
            if (building.TryGetComp<CompPowerPlant>() != null)
            {
                PowerUp(building);
            }

            building.TryGetComp<CompPowerBattery>()?.SetStoredEnergyPct(1f);
        }
    }

    private static void PowerUp(Building building)
    {
        var flickable = building.TryGetComp<CompFlickable>();
        if (flickable != null)
        {
            flickable.SwitchIsOn = true;
        }

        var refuelable = building.TryGetComp<CompRefuelable>();
        if (refuelable != null)
        {
            refuelable.Refuel(refuelable.Props.fuelCapacity);
        }
    }

    private List<Building> AllBuildings()
    {
        var buildings = new List<Building>();
        buildings.AddRange(map.listerBuildings.allBuildingsNonColonist);
        buildings.AddRange(map.listerBuildings.allBuildingsColonist);
        return buildings;
    }

    private void SendSalvagers()
    {
        var faction = SalvagerFaction();
        if (faction == null)
        {
            return;
        }

        var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
        parms.forced = true;
        parms.faction = faction;
        parms.points = ambushPoints > 0f ? ambushPoints : StorytellerUtility.DefaultThreatPointsNow(map) * 0.6f;
        parms.raidStrategy = Abhuman40kDefOf.ImmediateAttackSmart;
        parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
        parms.canSteal = true;
        parms.canKidnap = false;
        parms.customLetterLabel = "BEWH.Abhuman.Reactor.SalvagersLetterLabel".Translate();
        parms.customLetterText = "BEWH.Abhuman.Reactor.SalvagersLetterText".Translate(faction.Name);

        IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
    }

    private Faction SalvagerFaction()
    {
        var parent = map.ParentFaction;
        if (IsValidSalvager(parent))
        {
            return parent;
        }

        var random = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        return IsValidSalvager(random) ? random : null;
    }

    private static bool IsValidSalvager(Faction faction)
    {
        return faction != null && faction != Faction.OfMechanoids && faction.HostileTo(Faction.OfPlayer);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref navigator, "navigator", saveDestroyedThings: true);
        Scribe_Values.Look(ref ambushPoints, "ambushPoints", -1f);
        Scribe_Values.Look(ref triggered, "triggered");
        Scribe_Values.Look(ref turretsAwake, "turretsAwake");
        Scribe_Collections.Look(ref shipTurrets, "shipTurrets", LookMode.Reference);
        Scribe_Values.Look(ref lastSeenOnThisMap, "lastSeenOnThisMap", true);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            shipTurrets ??= new List<Thing>();
            shipTurrets.RemoveAll(turret => turret == null);
        }
    }
}
