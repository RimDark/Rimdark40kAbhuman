using Core40k;
using Verse;

namespace Abhuman40k;

/// <summary>
/// Legacy state holder for the downed navigator quest. The quest itself now runs on the
/// framework's persistent quest registry; this component only carries pre-existing saves over.
/// </summary>
public class GameComponent_NavigatorQuest : GameComponent
{
    public const string NavigatorIncidentDefName = "BEWH_NavigatorDowned";

    private bool navigatorSecured;
    private int nextEarliestFireTick = -1;
    private int trackedQuestId = -1;
    private bool migrated;

    public GameComponent_NavigatorQuest(Game game)
    {
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();

        if (migrated)
        {
            return;
        }

        migrated = true;

        if (!navigatorSecured && nextEarliestFireTick < 0 && trackedQuestId < 0)
        {
            return;
        }

        var occurrences = navigatorSecured || trackedQuestId >= 0 || nextEarliestFireTick > 0 ? 1 : 0;
        GameComponent_PersistentQuests.SeedState(NavigatorIncidentDefName, navigatorSecured, nextEarliestFireTick, trackedQuestId, occurrences);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref navigatorSecured, "navigatorSecured");
        Scribe_Values.Look(ref nextEarliestFireTick, "nextEarliestFireTick", -1);
        Scribe_Values.Look(ref trackedQuestId, "trackedQuestId", -1);
        Scribe_Values.Look(ref migrated, "migratedToPersistentQuests");
    }
}
