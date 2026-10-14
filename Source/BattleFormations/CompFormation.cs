using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BattleFormations
{
    public class CompProperties_Formation : CompProperties
    {
        public CompProperties_Formation()
        {
            compClass = typeof(CompFormation);
        }
    }

    // Lives on every humanlike pawn: stores the manual role and adds the formation gizmo while drafted.
    public class CompFormation : ThingComp
    {
        public RoleOverride roleOverride = RoleOverride.Auto;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            Pawn pawn = parent as Pawn;
            if (pawn == null || !pawn.Drafted || !pawn.IsColonistPlayerControlled)
                yield break;
            yield return new Command_Formation();

            Regiment reg = GameComponent_Regiments.Get().RegimentOf(new List<Pawn> { pawn });
            if (reg != null)
            {
                // Captured before the click, so a grouped button sets every regiment the same way.
                bool hold = reg.hold;
                Command_Toggle toggle = new Command_Toggle();
                toggle.defaultLabel = "BF_Hold".Translate();
                toggle.defaultDesc = "BF_HoldDesc".Translate();
                toggle.icon = reg.def != null && reg.def.Icon != null ? reg.def.Icon : BFTextures.FormationIcon;
                toggle.isActive = () => reg.hold;
                toggle.toggleAction = delegate { GameComponent_Regiments.SetHold(reg, !hold); };
                yield return toggle;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref roleOverride, "bfRoleOverride", RoleOverride.Auto);
        }
    }

    public class Command_Formation : Command
    {
        public Command_Formation()
        {
            defaultLabel = "BF_Command".Translate();
            defaultDesc = "BF_CommandDesc".Translate();
            icon = BFTextures.FormationIcon;
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            List<Pawn> pawns = FormationUtility.SelectedFormationPawns();
            if (pawns.Count == 0)
                return;
            Regiment reg = GameComponent_Regiments.Get().RegimentOf(pawns);
            FormationDef def = reg != null && reg.def != null ? reg.def : FormationMemory.LastOrDefault();
            Find.WindowStack.Add(new Window_FormationPlacement(pawns, def));
        }

        // One button for the whole selection, opened once.
        public override bool GroupsWith(Gizmo other)
        {
            return other is Command_Formation;
        }

        public override bool InheritInteractionsFrom(Gizmo other)
        {
            return false;
        }
    }

    [StaticConstructorOnStartup]
    public static class BFTextures
    {
        public static readonly Texture2D FormationIcon = ContentFinder<Texture2D>.Get("UI/BattleFormations/Formations/Phalanx", true);
        public static readonly Texture2D Info = ContentFinder<Texture2D>.Get("UI/Buttons/InfoButton", true);
    }

    // Adds the comp to all humanlike races (incl. modded ones) without Harmony.
    [StaticConstructorOnStartup]
    public static class BattleFormationsStartup
    {
        static BattleFormationsStartup()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race == null || !def.race.Humanlike)
                    continue;
                if (def.comps == null)
                    def.comps = new List<CompProperties>();
                if (!def.comps.Any(c => c is CompProperties_Formation))
                    def.comps.Add(new CompProperties_Formation());
            }
        }
    }
}
