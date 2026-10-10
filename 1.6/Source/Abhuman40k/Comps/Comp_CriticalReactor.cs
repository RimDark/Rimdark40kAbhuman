using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Abhuman40k;

public class Comp_CriticalReactor : ThingComp, IThingGlower
{
    private int ticksRemaining = -1;
    private int totalTicks = -1;
    private bool detonated;

    private IntVec3 lastPosition = IntVec3.Invalid;
    private CompGlower glower;
    private ColorInt? appliedGlowColor;

    public CompProperties_CriticalReactor Props => (CompProperties_CriticalReactor)props;

    private Building_CriticalReactor Reactor => parent as Building_CriticalReactor;

    public bool Armed => ticksRemaining >= 0;

    private bool Unstable => Reactor is { Stage: ReactorStage.Unstable };

    public bool ShouldBeLitNow()
    {
        return Unstable;
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        glower = parent.GetComp<CompGlower>();
        appliedGlowColor = null;
        RefreshGlow();
    }

    public void Notify_Destabilized()
    {
        if (Armed)
        {
            return;
        }

        totalTicks = Props.ticksToExplode.RandomInRange;
        ticksRemaining = totalTicks;
        appliedGlowColor = null;

        if (parent.Spawned && glower != null)
        {
            glower.UpdateLit(parent.Map);
        }

        RefreshGlow();
    }

    public override void CompTickInterval(int delta)
    {
        base.CompTickInterval(delta);

        if (detonated || !Armed || !Unstable)
        {
            return;
        }

        ticksRemaining -= delta;

        if (ticksRemaining <= 0)
        {
            parent.Destroy(DestroyMode.KillFinalize);
            return;
        }

        if (parent.IsHashIntervalTick(Props.glowUpdateInterval, delta))
        {
            RefreshGlow();
        }
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        lastPosition = parent.Position;
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        base.PostDestroy(mode, previousMap);

        if (detonated || mode != DestroyMode.KillFinalize)
        {
            return;
        }

        detonated = true;
        Blast(previousMap, lastPosition.IsValid ? lastPosition : parent.Position);
    }

    private void Blast(Map map, IntVec3 center)
    {
        if (map == null)
        {
            return;
        }

        // Levelling runs first on purpose. GenExplosion computes the cells it will hit at spawn
        // time and drops any cell without line of sight to the centre, so an explosion fired
        // while the ship is still standing stops at the first bulkhead.
        Level(map, center);

        GenExplosion.DoExplosion(center, map, Props.explosionRadius, Props.damageDef ?? DamageDefOf.Bomb, parent,
            Props.damageAmount,
            postExplosionSpawnSingleThingDef: Props.craterDef,
            chanceToStartFire: Props.chanceToStartFire,
            damageFalloff: false,
            screenShakeFactor: Props.screenShakeFactor);

        if (Props.detonationLetter != null && map.mapPawns.FreeColonistsSpawnedCount > 0)
        {
            Find.LetterStack.ReceiveLetter("BEWH.Abhuman.Reactor.DetonationLetterLabel".Translate(),
                "BEWH.Abhuman.Reactor.DetonationLetterText".Translate(), Props.detonationLetter,
                new TargetInfo(center, map));
        }
    }

    private void Level(Map map, IntVec3 center)
    {
        var targets = GenRadial.RadialDistinctThingsAround(center, map, Props.explosionRadius, true)
            .Where(IsLevellable)
            .ToList();

        foreach (var thing in targets)
        {
            if (!thing.Spawned || thing.Destroyed)
            {
                continue;
            }

            if (IsTough(thing))
            {
                if (!thing.def.useHitPoints)
                {
                    continue;
                }

                var survivingHitPoints = Mathf.Max(1, Mathf.RoundToInt(thing.MaxHitPoints * Props.survivorHitPointsFactor));
                if (thing.HitPoints > survivingHitPoints)
                {
                    thing.HitPoints = survivingHitPoints;
                }

                continue;
            }

            thing.Destroy(DestroyMode.KillFinalize);
        }
    }

    private bool IsLevellable(Thing thing)
    {
        if (thing == parent || thing.Destroyed || !thing.Spawned)
        {
            return false;
        }

        if (thing is Building_CriticalReactor)
        {
            return false;
        }

        if (thing.def.category != ThingCategory.Building && thing.def.category != ThingCategory.Plant)
        {
            return false;
        }

        return thing.def.building is not { isNaturalRock: true };
    }

    private bool IsTough(Thing thing)
    {
        if (Props.toughThingDefs != null && Props.toughThingDefs.Contains(thing.def))
        {
            return true;
        }

        var stuffProps = thing.Stuff?.stuffProps;
        if (stuffProps?.statFactors == null)
        {
            return false;
        }

        return stuffProps.statFactors.GetStatFactorFromList(StatDefOf.MaxHitPoints) >= Props.toughnessThreshold;
    }

    private void RefreshGlow()
    {
        if (glower == null || !parent.Spawned || !Unstable || Props.glowStops.NullOrEmpty())
        {
            return;
        }

        var color = GlowColorFor(Progress);

        if (appliedGlowColor.HasValue && appliedGlowColor.Value.Equals(color))
        {
            return;
        }

        appliedGlowColor = color;
        glower.GlowColor = color;
    }

    private float Progress => totalTicks <= 0 ? 1f : Mathf.Clamp01(1f - (float)ticksRemaining / totalTicks);

    private ColorInt GlowColorFor(float progress)
    {
        List<ReactorGlowStop> stops = Props.glowStops;

        var previous = stops[0];
        foreach (var stop in stops)
        {
            if (stop.progress > progress)
            {
                var span = stop.progress - previous.progress;
                var t = span <= 0f ? 1f : Mathf.Clamp01((progress - previous.progress) / span);
                return Lerp(previous.color, stop.color, t);
            }

            previous = stop;
        }

        return previous.color;
    }

    private static ColorInt Lerp(ColorInt from, ColorInt to, float t)
    {
        return new ColorInt(
            Mathf.RoundToInt(Mathf.Lerp(from.r, to.r, t)),
            Mathf.RoundToInt(Mathf.Lerp(from.g, to.g, t)),
            Mathf.RoundToInt(Mathf.Lerp(from.b, to.b, t)),
            Mathf.RoundToInt(Mathf.Lerp(from.a, to.a, t)));
    }

    public override string CompInspectStringExtra()
    {
        var text = Unstable
            ? "BEWH.Abhuman.Reactor.InspectUnstable".Translate().Colorize(ColorLibrary.RedReadable)
            : "BEWH.Abhuman.Reactor.InspectDormant".Translate().ToString();

        if (DebugSettings.godMode && Armed)
        {
            text += "\n" + ticksRemaining.ToStringTicksToPeriod();
        }

        return text;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", -1);
        Scribe_Values.Look(ref totalTicks, "totalTicks", -1);
        Scribe_Values.Look(ref detonated, "detonated");
        Scribe_Values.Look(ref lastPosition, "lastPosition", IntVec3.Invalid);
    }
}
