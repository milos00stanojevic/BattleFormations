using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace BattleFormations
{
    // Info card for a formation: where it comes from, who used it, famous battles.
    public class Dialog_FormationInfo : Window
    {
        readonly FormationDef def;
        Vector2 scroll;

        public override Vector2 InitialSize
        {
            get { return new Vector2(580f, 520f); }
        }

        // Sits above the formation bar instead of covering it.
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            float barTop = UI.screenHeight - 125f - Window_FormationPlacement.BarHeight;
            float y = Mathf.Max(10f, Mathf.Min((UI.screenHeight - size.y) / 2f, barTop - 12f - size.y));
            windowRect = new Rect((UI.screenWidth - size.x) / 2f, y, size.x, size.y).Rounded();
        }

        public Dialog_FormationInfo(FormationDef def)
        {
            this.def = def;
            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        // Small "i" button like on vanilla items.
        public static void InfoButton(Rect rect, FormationDef def)
        {
            TooltipHandler.TipRegion(rect, "BF_Info".Translate());
            if (Widgets.ButtonImage(rect, BFTextures.Info))
                Find.WindowStack.Add(new Dialog_FormationInfo(def));
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Header: icon, name, era
            Rect iconRect = new Rect(inRect.x, inRect.y, 64f, 64f);
            Widgets.DrawOptionBackground(iconRect, false);
            if (def.Icon != null)
                GUI.DrawTexture(iconRect.ContractedBy(4f), def.Icon);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(iconRect.xMax + 12f, inRect.y + 4f, inRect.width - 90f, 34f), def.LabelCap);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(iconRect.xMax + 12f, inRect.y + 38f, inRect.width - 90f, 24f), ("BF_Era_" + def.era).Translate());
            GUI.color = Color.white;

            float y = iconRect.yMax + 12f;
            y = Row(inRect, y, "BF_InfoOrigin", def.origin);
            y = Row(inRect, y, "BF_InfoPeriod", def.period);
            y = Row(inRect, y, "BF_InfoBattles", def.battles);
            y = Row(inRect, y, "BF_InfoTactic", TacticText(def));
            Widgets.DrawLineHorizontal(inRect.x, y + 4f, inRect.width);
            y += 12f;

            // History text, scrollable
            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            string text = def.history.NullOrEmpty() ? def.description : def.history + "\n\n" + def.description;
            float height = Text.CalcHeight(text, outRect.width - 16f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, height);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            Widgets.Label(viewRect, text);
            Widgets.EndScrollView();
        }

        public static string TacticText(FormationDef def)
        {
            return ("BF_Tactic_" + def.tactic).Translate() + ": " + ("BF_Tactic_" + def.tactic + "Desc").Translate();
        }

        static float Row(Rect inRect, float y, string key, string value)
        {
            if (value.NullOrEmpty())
                return y;
            const float labelWidth = 130f;
            float h = Mathf.Max(24f, Text.CalcHeight(value, inRect.width - labelWidth));
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(inRect.x, y, labelWidth, 24f), key.Translate());
            GUI.color = Color.white;
            Widgets.Label(new Rect(inRect.x + labelWidth, y, inRect.width - labelWidth, h), value);
            return y + h + 2f;
        }
    }
}
