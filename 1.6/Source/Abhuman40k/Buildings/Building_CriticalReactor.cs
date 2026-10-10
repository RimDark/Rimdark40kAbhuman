using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Abhuman40k;

public enum ReactorStage
{
    Dormant,
    Unstable
}

public class Building_CriticalReactor : Building
{
    private ReactorStage stage = ReactorStage.Dormant;

    private Comp_CriticalReactor reactorComp;
    private Graphic unstableGraphic;

    public ReactorStage Stage => stage;

    private Comp_CriticalReactor Reactor => reactorComp ??= GetComp<Comp_CriticalReactor>();

    public override Graphic Graphic
    {
        get
        {
            if (stage != ReactorStage.Unstable)
            {
                return base.Graphic;
            }

            var graphicData = Reactor?.Props.unstableGraphicData;
            if (graphicData == null)
            {
                return base.Graphic;
            }

            return unstableGraphic ??= graphicData.GraphicColoredFor(this);
        }
    }

    public void NotifyDestabilized()
    {
        if (stage != ReactorStage.Dormant)
        {
            return;
        }

        stage = ReactorStage.Unstable;
        unstableGraphic = null;

        Reactor?.Notify_Destabilized();

        if (Spawned)
        {
            Map.GetComponent<MapComponent_NavigatorRescue>()?.Notify_ReactorDestabilized();
            DirtyMapMesh(Map);
            Messages.Message("BEWH.Abhuman.Reactor.Destabilized".Translate(), this, MessageTypeDefOf.ThreatBig,
                historical: false);
        }
    }

    public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
    {
        base.PostApplyDamage(dinfo, totalDamageDealt);

        if (Spawned && totalDamageDealt > 0f)
        {
            NotifyDestabilized();
        }
    }

    public static void DestabilizeAllOnMap(Map map)
    {
        if (map == null)
        {
            return;
        }

        foreach (var reactor in ReactorsOn(map))
        {
            reactor.NotifyDestabilized();
        }
    }

    public static List<Building_CriticalReactor> ReactorsOn(Map map)
    {
        var reactors = new List<Building_CriticalReactor>();
        if (map == null)
        {
            return reactors;
        }

        foreach (var building in map.listerBuildings.allBuildingsNonColonist)
        {
            if (building is Building_CriticalReactor reactor)
            {
                reactors.Add(reactor);
            }
        }

        foreach (var building in map.listerBuildings.allBuildingsColonist)
        {
            if (building is Building_CriticalReactor reactor)
            {
                reactors.Add(reactor);
            }
        }

        return reactors;
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var gizmo in base.GetGizmos())
        {
            yield return gizmo;
        }

        if (!DebugSettings.godMode || stage != ReactorStage.Dormant)
        {
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "DEV: Destabilize reactor",
            defaultDesc = "Starts the hidden countdown and the glow ramp.",
            action = NotifyDestabilized
        };
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref stage, "stage", ReactorStage.Dormant);
    }
}
