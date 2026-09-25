using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Abhuman40k;

[StaticConstructorOnStartup]
public class WarpTravelWorldObject : WorldObject, IThingHolder
{
    public ThingOwner<Pawn> pawns;
    public int arrivalTick;

    // The length of this passage, kept so the arrival letter can report it. Reporting arrivalTick
    // would report ticks since the game began instead.
    public int travelDurationTicks;

    // How vague the navigator's own estimate is. The window shown to the player is
    // (remaining + estimateOffsetTicks) plus or minus estimateSpreadTicks, so the true arrival
    // sits somewhere inside it rather than at its centre. At 0 spread the estimate is exact.
    public int estimateSpreadTicks;
    public int estimateOffsetTicks;
    
    public Pawn Navigator
    {
        get
        {
            return pawns?.InnerListForReading.FirstOrFallback(p => p.genes != null && p.genes.HasActiveGene(Abhuman40kDefOf.BEWH_NavigtorWarpNavigation));
        }
    }
    
    public WarpTravelWorldObject()
    {
        pawns = new ThingOwner<Pawn>(this, oneStackOnly: false, LookMode.Reference);
    }
    
    public override bool SelectableNow => false;
    public override bool NeverMultiSelect => true;

    public override void Draw() {}
    
    public override IEnumerable<Gizmo> GetGizmos()
    {
        if (!DebugSettings.ShowDevGizmos)
        {
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "DEV: Conclude travel",
            action = delegate
            {
                arrivalTick = Find.TickManager.TicksGame;
            }
        };
    }

    public override string GetInspectString()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(base.GetInspectString());
        if (stringBuilder.Length != 0)
        {
            stringBuilder.AppendLine();
        }
        GetArrivalEstimate(out var lowerEstimate, out var higherEstimate);

        if (estimateSpreadTicks <= 0)
        {
            stringBuilder.Append("BEWH.Abhuman.Navigator.TravelTimeRemainingExact".Translate(lowerEstimate.ToStringTicksToPeriod()));
            return stringBuilder.ToString();
        }

        stringBuilder.Append("BEWH.Abhuman.Navigator.TravelTimeRemaining".Translate(lowerEstimate.ToStringTicksToPeriod(), higherEstimate.ToStringTicksToPeriod()));
        return stringBuilder.ToString();
    }
    
    /// <summary>
    /// The navigator's estimated window, in ticks from now, for when the passage ends. Both values are equal when the estimate is exact.
    /// </summary>
    public void GetArrivalEstimate(out int lowerEstimate, out int higherEstimate)
    {
        var remaining = Mathf.Max(0, arrivalTick - Find.TickManager.TicksGame);
        if (estimateSpreadTicks <= 0)
        {
            lowerEstimate = remaining;
            higherEstimate = remaining;
            return;
        }

        var centre = remaining + estimateOffsetTicks;
        lowerEstimate = Mathf.Max(0, centre - estimateSpreadTicks);
        higherEstimate = Mathf.Max(lowerEstimate, centre + estimateSpreadTicks);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref pawns, "pawns", this);
        Scribe_Values.Look(ref arrivalTick, "arrivalTick");
        Scribe_Values.Look(ref travelDurationTicks, "travelDurationTicks");
        Scribe_Values.Look(ref estimateSpreadTicks, "estimateSpreadTicks");
        Scribe_Values.Look(ref estimateOffsetTicks, "estimateOffsetTicks");
    }
    
    protected override void TickInterval(int delta)
    {
        base.TickInterval(delta);
        
        if (arrivalTick > Current.Game.tickManager.TicksGame)
        {
            return;
        }

        ExitMessage();
        ArriveAtPlace();
        Destroy();
    }

    private void ArriveAtPlace()
    {
        foreach (var pawnFaction in pawns.InnerListForReading.Where(pawnFaction => !pawnFaction.IsPrisoner && pawnFaction.Faction != Faction.OfPlayer))
        {
            pawnFaction.SetFaction(Faction.OfPlayer);
        }
        var target = Tile;
        var targetMap = Find.WorldObjects.MapParentAt(target)?.Map;
        var targetCaravan = Find.WorldObjects.PlayerControlledCaravanAt(target);
        var targetCell = IntVec3.Invalid;
        if (targetMap != null)
        {
            var alliedPawnOnMap = AlliedPawnOnMap(targetMap, pawns.InnerListForReading);
            if (alliedPawnOnMap != null)
            {
                targetCell = alliedPawnOnMap.Position;
            }
        }
        //Another allied pawn is on map which is being teleported too
        if (targetCell.IsValid)
        {
            for (var i = pawns.InnerListForReading.Count-1; i >= 0; i--)
            {
                var pawnToSpawn = pawns.InnerListForReading[i];
                if (!CellFinder.TryFindRandomSpawnCellForPawnNear(targetCell, targetMap, out var result, 4, cell => cell != targetCell && cell.GetRoom(targetMap) == targetCell.GetRoom(targetMap))
                    && !CellFinder.TryFindRandomSpawnCellForPawnNear(targetCell, targetMap, out result))
                {
                    result = targetCell;
                }

                GenSpawn.Spawn(pawnToSpawn, result, targetMap);
                if (pawnToSpawn.drafter != null && pawnToSpawn.IsColonistPlayerControlled)
                {
                    pawnToSpawn.drafter.Drafted = true;
                }
                if (pawnToSpawn.IsPrisoner)
                {
                    pawnToSpawn.guest.WaitInsteadOfEscapingForDefaultTicks();
                }
                if ((pawnToSpawn.IsColonist || pawnToSpawn.RaceProps.packAnimal) && pawnToSpawn.Map.IsPlayerHome)
                {
                    pawnToSpawn.inventory.UnloadEverything = true;
                }
            }
        }
        //Teleport to friendly caravan on world map
        else if (targetCaravan != null)
        {
            targetCaravan.pawns.TryAddRangeOrTransfer(pawns);
        }
        //Teleport to unoccupied world map tile
        else
        {
            var newCaravan = CaravanMaker.MakeCaravan(new List<Pawn>(), Faction.OfPlayer, Tile, false);
            newCaravan.pawns.TryAddRangeOrTransfer(pawns);
        }
    }
    
    private void ExitMessage()
    {
        var travelers = pawns.InnerListForReading;
        var teleportedPawns = travelers.Select(traveler => traveler.NameShortColored.Resolve()).ToCommaList(useAnd: true);

        var navigator = Navigator;
        if (navigator == null)
        {
            // The navigator did not survive the passage. Nothing to address the letter to.
            return;
        }

        var timeSpent = travelDurationTicks.ToStringTicksToPeriod();

        // LetterMaker assigns the unique load ID. Building the letter with an object initializer
        // leaves Letter.ID at 0, and two such letters in the archive collide on "Letter_0".
        var letter = LetterMaker.MakeLetter(
            "BEWH.Abhuman.Navigator.WarpTravelLetter".Translate(),
            "BEWH.Abhuman.Navigator.WarpTravelMessage".Translate(navigator.Named("PAWN"), timeSpent, teleportedPawns),
            Abhuman40kDefOf.BEWH_WarpTravel,
            navigator);

        Find.LetterStack.ReceiveLetter(letter);
    }

    public void AddPawn(Pawn p, bool addCarriedPawnToWorldPawnsIfAny)
    {
        if (p == null)
        {
            Log.Warning("Tried to add a null pawn to " + this);
            return;
        }
        if (p.Dead)
        {
            Log.Warning("Tried to add " + p + " to " + this + ", but this pawn is dead.");
            return;
        }
        var pawn = p.carryTracker.CarriedThing as Pawn;
        if (pawn != null)
        {
            p.carryTracker.innerContainer.Remove(pawn);
        }
        p.DeSpawnOrDeselect();
        if (pawns.TryAddOrTransfer(p))
        {
            if (pawn != null)
            {
                AddPawn(pawn, addCarriedPawnToWorldPawnsIfAny);
                if (addCarriedPawnToWorldPawnsIfAny)
                {
                    Find.WorldPawns.PassToWorld(pawn);
                }
            }
        }
        else
        {
            Log.Error("Couldn't add pawn " + p + " to caravan.");
        }
    }

    public bool ContainsPawn(Pawn p)
    {
        return pawns.Contains(p);
    }
    
    private Pawn AlliedPawnOnMap(Map targetMap, List<Pawn> pawnsToTeleport)
    {
        return targetMap.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !p.NonHumanlikeOrWildMan() && p.IsColonist && p.HomeFaction == Faction.OfPlayer && !pawnsToTeleport.Contains(p));
    }

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return pawns;
    }
}