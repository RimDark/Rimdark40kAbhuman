using RimWorld.Planet;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using System.Linq;

namespace Abhuman40k;

public class Ability_WarpEyeWarpTravel : VEF.Abilities.Ability
{
    // The raw roll, before the navigator's own stats touch it: a few seconds to roughly ten days.
    private readonly IntRange travelDurationRange = new IntRange(300, 600000);

    // How far the true duration can swing either side of the roll at 100% instability,
    // and how wide the arrival estimate shown to the player is.
    private const float BaseInstabilitySpreadTicks = 60000f;

    private const int MinTravelDurationTicks = 300;

    private const float TravelDurationWeightExponent = 2f;

    private int Capacity => Mathf.Max(1, Mathf.RoundToInt(CasterPawn.GetStatValue(Abhuman40kDefOf.BEWH_WarpTravelCapacity)));

    private int previewFrame = -1;
    private List<Pawn> previewCandidates;
    private HashSet<Pawn> previewTravelers;

    public override void DoAction()
    {
        var lodger = PawnsToTravel().FirstOrDefault(p => p.IsQuestLodger());
        if (lodger != null)
        {
            Dialog_MessageBox.CreateConfirmation("FarskipConfirmTeleportingLodger".Translate(lodger.Named("PAWN")), base.DoAction);
        }
        else
        {
            base.DoAction();
        }
    }

    private IEnumerable<Pawn> PawnsToTeleport()
    {
        var caravan = CasterPawn.GetCaravan();
        if (caravan != null)
        {
            foreach (var caravanPawn in caravan.pawns)
            {
                yield return caravanPawn;
            }
            yield break;
        }
        var map = CasterPawn.Map;
        if (map == null)
        {
            yield break;
        }

        var homeMap = map.IsPlayerHome;
        foreach (var thing in GenRadial.RadialDistinctThingsAround(CasterPawn.Position, map, GetRadiusForPawn(), useCenter: true))
        {
            if (thing is Pawn mapPawn && !mapPawn.Dead && (mapPawn.IsColonist || mapPawn.IsPrisonerOfColony || (!homeMap && mapPawn.RaceProps.Animal && (mapPawn.Faction?.IsPlayer ?? false))))
            {
                yield return mapPawn;
            }
        }
    }

    /// <summary>
    /// Everyone in range, trimmed to what the navigator can actually hold in the warp.
    /// The navigator always travels - without them there is no passage - and the rest fill the
    /// remaining places nearest first.
    /// </summary>
    private List<Pawn> PawnsToTravel()
    {
        return LimitToCapacity(PawnsToTeleport().ToList());
    }

    private List<Pawn> LimitToCapacity(List<Pawn> candidates)
    {
        var capacity = Capacity;
        if (candidates.Count <= capacity)
        {
            return candidates;
        }

        candidates = new List<Pawn>(candidates);
        candidates.Remove(CasterPawn);

        if (CasterPawn.Spawned)
        {
            var origin = CasterPawn.Position;
            candidates.SortBy(p => p.Spawned ? (p.Position - origin).LengthHorizontalSquared : int.MaxValue);
        }

        var travelers = new List<Pawn> { CasterPawn };
        travelers.AddRange(candidates.Take(capacity - 1));
        return travelers;
    }

    public override bool CanHitTargetTile(GlobalTargetInfo target)
    {
        var range = Find.WorldGrid.TraversalDistanceBetween((CasterPawn.GetCaravan() != null) ? CasterPawn.GetCaravan().Tile : Caster.Map.Tile, target.Tile);
        return !(range > GetRangeForPawn());
    }

    public override void Cast(params GlobalTargetInfo[] targets)
    {
        base.Cast(targets);
        var caravan = pawn.GetCaravan();

        var candidates = PawnsToTeleport().ToList();
        var travelingPawns = LimitToCapacity(candidates);
        var leftBehind = candidates.Where(p => !travelingPawns.Contains(p)).ToList();

        // Duration is rolled, then scaled - never the absolute game tick, which is what the old
        // House Achelieux halving did and why late-game translations finished instantly.
        var durationFactor = CasterPawn.GetStatValue(Abhuman40kDefOf.BEWH_WarpTravelDurationFactor);
        var instability = Mathf.Max(0f, CasterPawn.GetStatValue(Abhuman40kDefOf.BEWH_WarpTravelInstability));

        var spread = BaseInstabilitySpreadTicks * instability;
        var duration = Mathf.RoundToInt(Mathf.Max(MinTravelDurationTicks, RollWeightedDuration() * durationFactor + Rand.Range(-spread, spread)));

        var warpTravel = Abhuman40kUtils.MakeWarpTravelObject(travelingPawns, targets[0].Tile, duration, false);
        warpTravel.estimateSpreadTicks = Mathf.RoundToInt(spread);
        warpTravel.estimateOffsetTicks = Mathf.RoundToInt(Rand.Range(-spread, spread));

        foreach (var travelingPawn in travelingPawns)
        {
            if (!travelingPawn.IsWorldPawn())
            {
                travelingPawn.ExitMap(false, Rot4.Invalid);
            }
        }

        SendDepartureMessage(warpTravel, travelingPawns.Count > 1);

        if (leftBehind.Any())
        {
            Messages.Message("BEWH.Abhuman.Navigator.WarpTravelOverCapacity".Translate(CasterPawn.Named("PAWN"), Capacity, leftBehind.Count), CasterPawn, MessageTypeDefOf.CautionInput, historical: false);
        }

        // Only fold the caravan away once it is actually empty - anyone over capacity stays in it.
        if (caravan != null && !caravan.PawnsListForReading.Any())
        {
            caravan.RemoveAllPawns();
            caravan.Destroy();
        }
    }

    /// <summary>
    /// Rolls a base duration within travelDurationRange, weighted towards the shorter end.
    /// </summary>
    private float RollWeightedDuration()
    {
        return Mathf.Lerp(travelDurationRange.min, travelDurationRange.max, Mathf.Pow(Rand.Value, TravelDurationWeightExponent));
    }

    /// <summary>
    /// Announces the departure along with the navigator's estimate of how long the passage will take.
    /// </summary>
    private void SendDepartureMessage(WarpTravelWorldObject warpTravel, bool hasCompanions)
    {
        var departure = (hasCompanions ? "BEWH.Abhuman.Navigator.WarpTravelDepartedWithCompanions" : "BEWH.Abhuman.Navigator.WarpTravelDepartedAlone").Translate(CasterPawn.Named("PAWN"));

        warpTravel.GetArrivalEstimate(out var lowerEstimate, out var higherEstimate);
        var estimate = lowerEstimate == higherEstimate
            ? "BEWH.Abhuman.Navigator.WarpTravelDepartedEstimateExact".Translate(lowerEstimate.ToStringTicksToPeriod())
            : "BEWH.Abhuman.Navigator.WarpTravelDepartedEstimate".Translate(lowerEstimate.ToStringTicksToPeriod(), higherEstimate.ToStringTicksToPeriod());

        Messages.Message(departure + " " + estimate, MessageTypeDefOf.NeutralEvent, historical: true);
    }

    public override void GizmoUpdateOnMouseover()
    {
        if (WorldRendererUtility.WorldSelected)
        {
            return;
        }
        GenDraw.DrawRadiusRing(pawn.Position, GetRadiusForPawn(), Color.blue);

        // Both lists come from a radial sweep of the map, so build them once per frame rather
        // than twice per draw call.
        if (previewFrame != Time.frameCount || previewCandidates == null)
        {
            previewFrame = Time.frameCount;
            previewCandidates = PawnsToTeleport().ToList();
            previewTravelers = new HashSet<Pawn>(LimitToCapacity(previewCandidates));
        }

        foreach (var candidate in previewCandidates)
        {
            if (!candidate.Spawned)
            {
                continue;
            }
            // Green: coming along. Red: in range, but over the navigator's capacity.
            GenDraw.DrawRadiusRing(candidate.Position, 0.9f, previewTravelers.Contains(candidate) ? Color.green : Color.red);
        }
    }
}
