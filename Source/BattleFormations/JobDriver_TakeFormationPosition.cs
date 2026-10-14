using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BattleFormations
{
    // Walk to the slot, then turn to the formation's facing.
    public class JobDriver_TakeFormationPosition : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, job.targetA.Cell);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil face = new Toil();
            face.initAction = delegate
            {
                if (job.overrideFacing.IsValid)
                    pawn.Rotation = job.overrideFacing;
            };
            face.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return face;
        }
    }
}
