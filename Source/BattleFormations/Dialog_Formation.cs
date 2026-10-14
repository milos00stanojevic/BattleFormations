using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace BattleFormations
{
    public class Dialog_Formation : Window
    {
        readonly List<Pawn> pawns;
        readonly List<FormationDef> defs;
        FormationDef def;
        Vector2 scroll;

        public override Vector2 InitialSize
        {
            get { return new Vector2(780f, 580f); }
        }

        public Dialog_Formation(List<Pawn> pawns)
        {
            this.pawns = pawns;
            defs = DefDatabase<FormationDef>.AllDefsListForReading.OrderBy(d => d.order).ToList();
            def = FormationMemory.LastOrDefault();
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        FormationDef shortFor;
        string shortDesc;

        // Cut at a word boundary so it fits the box; the full text is in the tooltip.
        string ShortDescription(float width, float height)
        {
            if (shortFor == def && shortDesc != null)
                return shortDesc;
            string s = def.description ?? "";
            if (Text.CalcHeight(s, width) > height)
            {
                string[] words = s.Split(' ');
                int n = words.Length;
                do
                {
                    n--;
                    s = string.Join(" ", words, 0, n).TrimEnd(',', '.', ';', ':') + "...";
                } while (n > 1 && Text.CalcHeight(s, width) > height);
            }
            shortFor = def;
            shortDesc = s;
            return s;
        }

        int Width
        {
            get { return FormationMemory.Width(def); }
            set { FormationMemory.SetWidth(def, value); }
        }

        int Spacing
        {
            get { return FormationMemory.Spacing(def); }
            set { FormationMemory.SetSpacing(def, value); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (def == null)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 35f), "BF_DialogTitle".Translate());
            Text.Font = GameFont.Small;

            float top = inRect.y + 40f;
            float bottom = inRect.yMax - 40f;

            // Formation list
            Rect listRect = new Rect(inRect.x, top, 4 * (Window_FormationPlacement.ButtonSize + Window_FormationPlacement.ButtonGap), bottom - top);
            Window_FormationPlacement.DrawFormationGrid(listRect, defs, ref def, 4);

            // Settings + preview
            Rect right = new Rect(listRect.xMax + 12f, top, inRect.width - listRect.width - 12f, bottom - top);
            Dialog_FormationInfo.InfoButton(new Rect(right.x, right.y, 24f, 24f), def);
            Widgets.Label(new Rect(right.x + 30f, right.y, right.width - 30f, 24f), def.LabelCap);
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Rect descRect = new Rect(right.x, right.y + 24f, right.width, 44f);
            Widgets.Label(descRect, ShortDescription(descRect.width, descRect.height));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(descRect, def.description);

            float sy = right.y + 72f;
            Width = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(right.x, sy, right.width, 30f), Width, 1f, 40f,
                true, "BF_Width".Translate() + ": " + Width, null, null, 1f));
            sy += 34f;
            Spacing = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(right.x, sy, right.width, 30f), Spacing, 1f, 4f,
                true, "BF_Spacing".Translate() + ": " + Spacing, null, null, 1f));
            sy += 38f;

            FormationPlan plan = FormationUtility.MakePlan(pawns, def, Width, Spacing);

            float half = (right.width - 10f) / 2f;
            Rect previewRect = new Rect(right.x, sy, half, right.yMax - sy);
            Rect rolesRect = new Rect(right.x + half + 10f, sy, half, right.yMax - sy);
            DrawPreview(previewRect, plan);
            DrawRoles(rolesRect);

            // Buttons
            Rect placeRect = new Rect(inRect.xMax - 180f, inRect.yMax - 35f, 180f, 35f);
            if (Widgets.ButtonText(placeRect, "BF_Place".Translate()))
            {
                FormationMemory.lastDef = def;
                Close();
                Find.WindowStack.Add(new Window_FormationPlacement(pawns, def));
            }
        }

        static void DrawPreview(Rect rect, FormationPlan plan)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.08f, 0.8f));
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(rect.x, rect.y + 2f, rect.width, 24f), "BF_Front".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            if (plan.local.Count == 0)
                return;

            int minX = plan.local.Min(v => v.x), maxX = plan.local.Max(v => v.x);
            int minZ = plan.local.Min(v => v.z), maxZ = plan.local.Max(v => v.z);
            Rect area = rect.ContractedBy(10f);
            area.yMin += 20f;
            float cell = Mathf.Min(18f, area.width / (maxX - minX + 1), area.height / (maxZ - minZ + 1));
            float ox = area.center.x - (maxX - minX + 1) * cell / 2f;
            float oy = area.center.y - (maxZ - minZ + 1) * cell / 2f;
            for (int i = 0; i < plan.local.Count; i++)
            {
                IntVec3 v = plan.local[i];
                Rect r = new Rect(ox + (v.x - minX) * cell, oy + (maxZ - v.z) * cell, cell, cell).ContractedBy(1f);
                Widgets.DrawBoxSolid(r, FormationUtility.RoleColor(plan.roles[i]));
            }
        }

        void DrawRoles(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "BF_Roles".Translate());
            Rect outRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, pawns.Count * 30f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 0f;
            foreach (Pawn p in pawns)
            {
                Pawn pawn = p;
                Rect row = new Rect(0f, y, viewRect.width, 28f);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 6f, 6f, 16f), FormationUtility.RoleColor(FormationUtility.RoleOf(pawn)));
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x + 12f, row.y, row.width * 0.5f - 12f, row.height), pawn.LabelShortCap);
                Text.Anchor = TextAnchor.UpperLeft;
                Rect button = new Rect(row.x + row.width * 0.5f, row.y + 2f, row.width * 0.5f, 24f);
                if (Widgets.ButtonText(button, RoleButtonLabel(pawn)))
                    Find.WindowStack.Add(new FloatMenu(RoleOptions(pawn)));
                y += 30f;
            }
            Widgets.EndScrollView();
        }

        static string RoleButtonLabel(Pawn pawn)
        {
            CompFormation comp = pawn.GetComp<CompFormation>();
            if (comp == null || comp.roleOverride == RoleOverride.Auto)
                return "BF_Role_Auto".Translate() + " (" + FormationUtility.RoleLabel(FormationUtility.AutoRole(pawn)) + ")";
            return FormationUtility.RoleLabel(FormationUtility.RoleOf(pawn));
        }

        static List<FloatMenuOption> RoleOptions(Pawn pawn)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            CompFormation comp = pawn.GetComp<CompFormation>();
            if (comp == null)
                return options;
            options.Add(new FloatMenuOption("BF_Role_Auto".Translate(), delegate { comp.roleOverride = RoleOverride.Auto; }));
            options.Add(new FloatMenuOption(FormationUtility.RoleLabel(FormationRole.Pike), delegate { comp.roleOverride = RoleOverride.Pike; }));
            options.Add(new FloatMenuOption(FormationUtility.RoleLabel(FormationRole.Melee), delegate { comp.roleOverride = RoleOverride.Melee; }));
            options.Add(new FloatMenuOption(FormationUtility.RoleLabel(FormationRole.Ranged), delegate { comp.roleOverride = RoleOverride.Ranged; }));
            return options;
        }
    }
}
