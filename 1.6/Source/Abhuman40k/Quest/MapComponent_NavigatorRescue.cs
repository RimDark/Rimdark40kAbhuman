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
    private List<Thing> shipSystems = new();
    private readonly HashSet<Thing> excusedSystems = new();
    private bool meltdownStarted;
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

    /// <summary>
    /// Records a generator, battery or other piece of the wreck whose destruction sets off the reactor.
    /// </summary>
    public void RegisterShipSystem(Thing system)
    {
        if (system != null && !shipSystems.Contains(system))
        {
            shipSystems.Add(system);
        }
    }

    /// <summary>
    /// Called by the reactor whenever it starts its countdown, whatever set it off.
    /// </summary>
    public void Notify_ReactorDestabilized()
    {
        meltdownStarted = true;
        WakeTurrets();
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

        if (Find.TickManager.TicksGame % PollIntervalTicks != 0)
        {
            return;
        }

        if (turretsAwake)
        {
            SustainTurrets();
        }

        if (!meltdownStarted && (ShipSystemLost(shipSystems) || ShipSystemLost(shipTurrets)))
        {
            meltdownStarted = true;
            Building_CriticalReactor.DestabilizeAllOnMap(map);
            WakeTurrets();
        }

        if (navigator == null)
        {
            return;
        }

        if (navigator.Dead)
        {
            FailQuest();
            navigator = null;
            return;
        }

        if (navigator.Destroyed)
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
        else if (navigator.Dead)
        {
            FailQuest();
        }

        navigator = null;
    }

    /// <summary>
    /// True once a registered ship system has been destroyed outright. Anything the player
    /// deconstructs or uninstalls is excused, so dismantling the wreck does not set off the reactor.
    /// </summary>
    private bool ShipSystemLost(List<Thing> systems)
    {
        for (var i = 0; i < systems.Count; i++)
        {
            var system = systems[i];
            if (system == null || excusedSystems.Contains(system))
            {
                continue;
            }

            if (system.Destroyed)
            {
                return true;
            }

            if (!system.Spawned || map.designationManager.DesignationOn(system, DesignationDefOf.Deconstruct) != null)
            {
                excusedSystems.Add(system);
            }
        }

        return false;
    }

    /// <summary>
    /// Ends the ongoing quest tied to this site as failed once the navigator is dead.
    /// </summary>
    private void FailQuest()
    {
        var site = map.Parent;
        var quests = Find.QuestManager.QuestsListForReading;
        for (var i = 0; i < quests.Count; i++)
        {
            var quest = quests[i];
            if (quest.State != QuestState.Ongoing || quest.root != Abhuman40kDefOf.BEWH_NavigatorDowned)
            {
                continue;
            }

            if (!quest.PartsListForReading.Any(part => part is QuestPart_SpawnWorldObject spawn && spawn.worldObject == site))
            {
                continue;
            }

            var corpse = navigator.Corpse;
            Find.LetterStack.ReceiveLetter("BEWH.Abhuman.NavigatorRescue.DiedLetterLabel".Translate(),
                "BEWH.Abhuman.NavigatorRescue.DiedLetterText".Translate(navigator.Named("PAWN")),
                LetterDefOf.NegativeEvent, corpse is { Spawned: true } ? corpse : null, quest: quest);
            quest.End(QuestEndOutcome.Fail, sendLetter: false);
            return;
        }
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

        SustainTurrets();
    }

    /// <summary>
    /// Keeps the awakened ship turrets firing even when the wreck's grid can no longer feed them.
    /// Their draw is dropped to zero so the power net never picks them for a shutdown.
    /// </summary>
    private void SustainTurrets()
    {
        for (var i = 0; i < shipTurrets.Count; i++)
        {
            if (shipTurrets[i] is not ThingWithComps turret || !turret.Spawned || turret.Faction != Faction.OfMechanoids)
            {
                continue;
            }

            var power = turret.TryGetComp<CompPowerTrader>();
            if (power == null || power.PowerOn || !FlickUtility.WantsToBeOn(turret) || turret.IsBrokenDown())
            {
                continue;
            }

            power.PowerOutput = 0f;
            power.PowerOn = true;
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
        Scribe_Collections.Look(ref shipSystems, "shipSystems", LookMode.Reference);
        Scribe_Values.Look(ref meltdownStarted, "meltdownStarted");
        Scribe_Values.Look(ref lastSeenOnThisMap, "lastSeenOnThisMap", true);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            shipTurrets ??= new List<Thing>();
            shipTurrets.RemoveAll(turret => turret == null);
            shipSystems ??= new List<Thing>();
            shipSystems.RemoveAll(system => system == null);
        }
    }
}
