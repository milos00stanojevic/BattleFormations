using Verse;

namespace BattleFormations
{
    // Slows a faster regiment member to the pace of the slowest one while it walks to its place.
    // Gone as soon as the pawn arrives, gets another order or is undrafted.
    public class Hediff_MarchPace : Hediff
    {
        public override bool ShouldRemove
        {
            get { return base.ShouldRemove || !pawn.Drafted || pawn.CurJobDef != BFDefOf.BF_TakeFormationPosition; }
        }
    }
}
