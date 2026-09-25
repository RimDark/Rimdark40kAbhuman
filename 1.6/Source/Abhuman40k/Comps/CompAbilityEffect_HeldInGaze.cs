using RimWorld;
using Verse;

namespace Abhuman40k;

public class CompAbilityEffect_HeldInGaze : CompAbilityEffect
{
    public new CompProperties_AbilityHeldInGaze Props => (CompProperties_AbilityHeldInGaze)props;

    public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
    {
        var targetPawn = target.Pawn;
        if (targetPawn == null || targetPawn.Dead)
        {
            return;
        }

        targetPawn.stances?.stunner.StunFor(Props.stunPulseTicks * 2, parent.pawn, true, true, true);
        RefreshHeldHediff(targetPawn);
    }

    public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
    {
        var targetPawn = target.Pawn;
        if (targetPawn != null && IsPsychicallyDeaf(targetPawn))
        {
            if (throwMessages)
            {
                Messages.Message("BEWH.Abhuman.Navigator.HeldInGazeDeaf".Translate(targetPawn.Named("PAWN")), targetPawn, MessageTypeDefOf.RejectInput, false);
            }
            return false;
        }

        return base.Valid(target, throwMessages);
    }

    public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
    {
        return target.Pawn == null ? base.ExtraLabelMouseAttachment(target) : "BEWH.Abhuman.Navigator.WillBeHeld".Translate().ToString();
    }

    /// <summary>
    /// Adds or refreshes the informational hediff shown on a held target.
    /// </summary>
    public void RefreshHeldHediff(Pawn targetPawn)
    {
        if (Props.heldHediff == null || targetPawn.health == null)
        {
            return;
        }

        var hediff = targetPawn.health.GetOrAddHediff(Props.heldHediff);
        hediff.TryGetComp<HediffComp_Disappears>()?.ResetElapsedTicks();
    }

    public static bool IsPsychicallyDeaf(Pawn pawn)
    {
        return pawn.GetStatValue(StatDefOf.PsychicSensitivity) <= 0f;
    }

    /// <summary>
    /// True if the caster wears apparel covering any of the configured body part groups.
    /// </summary>
    public bool CasterHeadCovered()
    {
        var wornApparel = parent.pawn?.apparel?.WornApparel;
        if (wornApparel == null || Props.blockedIfCovered.NullOrEmpty())
        {
            return false;
        }

        for (var i = 0; i < wornApparel.Count; i++)
        {
            var groups = wornApparel[i].def.apparel.bodyPartGroups;
            for (var j = 0; j < Props.blockedIfCovered.Count; j++)
            {
                if (groups.Contains(Props.blockedIfCovered[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
