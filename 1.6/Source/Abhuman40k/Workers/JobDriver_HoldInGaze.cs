using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Abhuman40k;

/// <summary>
/// Casts the held-in-gaze ability, then channels indefinitely: the target is kept stunned while the
/// caster builds up strain. Ends when interrupted, when the caster goes down, or when the hold breaks.
/// </summary>
public class JobDriver_HoldInGaze : JobDriver
{
    private Pawn Target => job.GetTarget(TargetIndex.A).Pawn;

    private CompAbilityEffect_HeldInGaze GazeComp => job.ability?.CompOfType<CompAbilityEffect_HeldInGaze>();

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return true;
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => GazeComp == null);

        var stop = ToilMaker.MakeToil("HoldInGazeStop");
        stop.initAction = () => pawn.pather.StopDead();
        stop.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return stop;

        yield return Toils_Combat.CastVerb(TargetIndex.A, TargetIndex.B, false);

        var channel = ToilMaker.MakeToil("HoldInGazeChannel");
        channel.defaultCompleteMode = ToilCompleteMode.Never;
        channel.handlingFacing = true;
        channel.socialMode = RandomSocialMode.Off;
        channel.initAction = () =>
        {
            if (Target?.stances?.stunner.Stunned != true)
            {
                EndJobWith(JobCondition.Incompletable);
            }
        };
        channel.tickIntervalAction = delta =>
        {
            var comp = GazeComp;
            var target = Target;
            if (comp == null || target == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            var props = comp.Props;
            pawn.rotationTracker.FaceTarget(target);

            if (pawn.IsHashIntervalTick(props.checkIntervalTicks, delta) && !CanKeepHolding(comp, target))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (pawn.IsHashIntervalTick(props.stunPulseTicks, delta))
            {
                target.stances?.stunner.StunFor(props.stunPulseTicks * 2, pawn, false, true, true);
                comp.RefreshHeldHediff(target);
            }

            if (props.strainHediff != null)
            {
                var strain = pawn.health.GetOrAddHediff(props.strainHediff);
                strain.Severity += props.strainPerTick * props.bodySizeFactorRange.ClampToRange(target.BodySize) * delta;
            }
        };
        yield return channel;
    }

    private bool CanKeepHolding(CompAbilityEffect_HeldInGaze comp, Pawn target)
    {
        if (!target.Spawned || target.Dead || target.Downed || target.Map != pawn.Map)
        {
            return false;
        }

        if (!target.Position.InHorDistOf(pawn.Position, job.verbToUse?.verbProps.range ?? 10.9f))
        {
            return false;
        }

        if (comp.Props.requiresLineOfSight && !GenSight.LineOfSight(pawn.Position, target.Position, pawn.Map))
        {
            return false;
        }

        return !comp.CasterHeadCovered() && !CompAbilityEffect_HeldInGaze.IsPsychicallyDeaf(target);
    }
}
