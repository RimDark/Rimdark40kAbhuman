using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Abhuman40k;

public class CompProperties_AbilityHeldInGaze : CompProperties_AbilityEffect
{
    public HediffDef strainHediff;
    public HediffDef heldHediff;
    public float strainPerTick = 0.0006f;
    public FloatRange bodySizeFactorRange = new (0.5f, 3f);
    public int stunPulseTicks = 30;
    public int checkIntervalTicks = 60;
    public bool requiresLineOfSight = true;
    public List<BodyPartGroupDef> blockedIfCovered = new ();

    public CompProperties_AbilityHeldInGaze()
    {
        compClass = typeof(CompAbilityEffect_HeldInGaze);
    }
}
