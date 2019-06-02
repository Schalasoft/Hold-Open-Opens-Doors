using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace HOOD
{
    class Door_Console_Job_Driver : JobDriver
    {
        protected Door_Console doorConsole => (Door_Console)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = base.pawn;
            LocalTargetInfo target = doorConsole;
            Job job = base.job;
            return pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(delegate
            {
                doorConsole.StoringThing();
            });
        }
    }
}