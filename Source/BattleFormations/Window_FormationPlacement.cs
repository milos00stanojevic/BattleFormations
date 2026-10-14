using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BattleFormations
{
    // Session memory for the quick placement (last formation, width/spacing per formation).
    public static class FormationMemory
    {
        public static FormationDef lastDef;
        public static bool hold = true;
        static readonly Dictionary<FormationDef, int> widths = new Dictionary<FormationDef, int>();
        static readonly Dictionary<FormationDef, int> spacings = new Dictionary<FormationDef, int>();

        public static FormationDef LastOrDefault()
        {
            return lastDef ?? DefDatabase<FormationDef>.AllDefsListForReading.OrderBy(d => d.order).FirstOrDefault();
        }

        public static int Width(FormationDef def)
        {
            int v;
            return widths.TryGetValue(def, out v) ? v : def.defaultWidth;
        }

        public static void SetWidth(FormationDef def, int v)
        {
            widths[def] = Mathf.Clamp(v, 1, 40);
        }

        public static int Spacing(FormationDef def)
        {
            int v;
            return spacings.TryGetValue(def, out v) ? v : def.defaultSpacing;
        }

        public static void SetSpacing(FormationDef def, int v)
        {
            spacings[def] = Mathf.Clamp(v, 1, 4);
        }
    }

    // Full-screen overlay: preview follows the mouse. Click = place (facing = walking direction),
    // drag = draw the front line (width + facing, 8 directions), Q/E = turn, right-click/Esc = cancel.
    public class Window_FormationPlacement : Window
    {
        const float MinBarWidth = 900f;
        internal const float BarHeight = 122f;

        readonly List<Pawn> pawns;
        readonly List<FormationDef> defs;
        FormationDef def;
        int dir;
        bool manualDir;
        bool dragging;
        IntVec3 dragStart;
        readonly Dictionary<FormationRole, List<IntVec3>> previewCells = new Dictionary<FormationRole, List<IntVec3>>();

        public Window_FormationPlacement(List<Pawn> pawns, FormationDef def)
        {
            this.pawns = pawns;
            this.def = def;
            defs = DefDatabase<FormationDef>.AllDefsListForReading.OrderBy(d => d.order).ToList();
            Regiment reg = GameComponent_Regiments.Get().RegimentOf(pawns);
            dir = reg != null ? reg.dir : 0;

            layer = WindowLayer.GameUI;
            doWindowBackground = false;
            drawShadow = false;
            doCloseX = false;
            closeOnClickedOutside = false;
            closeOnCancel = true;
            closeOnAccept = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            soundAppear = null;
            soundClose = null;
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
        }

        Rect BarRect
        {
            get
            {
                // Wide enough for all icons in one row.
                float rowWidth = 12f;
                for (int i = 0; i < defs.Count; i++)
                    rowWidth += ButtonSize + ButtonGap + (i > 0 && defs[i].era != defs[i - 1].era ? EraGap : 0f);
                float w = Mathf.Max(MinBarWidth, rowWidth);
                return new Rect((UI.screenWidth - w) / 2f, UI.screenHeight - 35f - BarHeight - 90f, w, BarHeight);
            }
        }

        List<Pawn> LivePawns()
        {
            Map map = Find.CurrentMap;
            return pawns.Where(p => p.Spawned && p.Map == map && !p.Dead && p.Drafted).ToList();
        }

        // Current anchor (front center), facing and width, from mouse / drag state.
        void Compute(out IntVec3 anchor, out int curDir, out int width)
        {
            int spacing = FormationMemory.Spacing(def);
            IntVec3 mouse = UI.MouseCell();
            width = FormationMemory.Width(def);
            curDir = dir;
            anchor = mouse;

            if (dragging)
            {
                IntVec3 delta = mouse - dragStart;
                int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));
                if (steps >= 2)
                {
                    int lineDir = FormationUtility.DirFromVector(delta, 2);
                    curDir = (lineDir + 6) % 8; // drag left->right = face north
                    width = steps / spacing + 1;
                    anchor = new IntVec3((dragStart.x + mouse.x) / 2, 0, (dragStart.z + mouse.z) / 2);
                    return;
                }
                anchor = dragStart;
            }

            if (!manualDir)
            {
                List<Pawn> live = LivePawns();
                if (live.Count > 0)
                {
                    IntVec3 center = new IntVec3(
                        Mathf.RoundToInt((float)live.Average(p => p.Position.x)), 0,
                        Mathf.RoundToInt((float)live.Average(p => p.Position.z)));
                    IntVec3 delta = anchor - center;
                    if (delta.LengthHorizontalSquared >= 9)
                        curDir = FormationUtility.DirFromVector(delta, dir);
                }
            }
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            List<Pawn> live = LivePawns();
            if (live.Count == 0 || def == null)
            {
                Close();
                return;
            }

            IntVec3 anchor;
            int curDir, width;
            Compute(out anchor, out curDir, out width);
            FormationPlan plan = FormationUtility.MakePlan(live, def, width, FormationMemory.Spacing(def));

            Map map = Find.CurrentMap;
            foreach (List<IntVec3> list in previewCells.Values)
                list.Clear();
            for (int i = 0; i < plan.local.Count; i++)
            {
                IntVec3 cell = FormationUtility.ToWorld(plan.local[i], anchor, curDir);
                if (!cell.InBounds(map))
                    continue;
                List<IntVec3> list;
                if (!previewCells.TryGetValue(plan.roles[i], out list))
                {
                    list = new List<IntVec3>();
                    previewCells[plan.roles[i]] = list;
                }
                list.Add(cell);
            }
            foreach (KeyValuePair<FormationRole, List<IntVec3>> kv in previewCells)
            {
                if (kv.Value.Count > 0)
                    GenDraw.DrawFieldEdges(kv.Value, FormationUtility.RoleColor(kv.Key));
            }
            Vector3 from = anchor.ToVector3Shifted();
            Vector3 to = (anchor + FormationUtility.DirVector(curDir) * 3).ToVector3Shifted();
            GenDraw.DrawLineBetween(from, to, SimpleColor.White);
        }

        public override void DoWindowContents(Rect inRect)
        {
            DrawBar(BarRect);

            Event ev = Event.current;
            bool overBar = BarRect.Contains(ev.mousePosition);

            // Info card open: no placing/turning underneath it.
            if (Find.WindowStack.IsOpen<Dialog_FormationInfo>())
            {
                dragging = false;
                return;
            }

            if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
            {
                dir = (dir + 7) % 8;
                manualDir = true;
                ev.Use();
            }
            else if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
            {
                dir = (dir + 1) % 8;
                manualDir = true;
                ev.Use();
            }
            else if (ev.type == EventType.MouseDown && ev.button == 1)
            {
                Close();
                ev.Use();
            }
            else if (ev.type == EventType.MouseDown && ev.button == 0 && !overBar)
            {
                dragging = true;
                dragStart = UI.MouseCell();
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0 && dragging)
            {
                Commit();
                ev.Use();
            }

            if (!overBar)
                Widgets.MouseAttachedLabel("BF_PlaceHint".Translate());
        }

        void Commit()
        {
            IntVec3 anchor;
            int curDir, width;
            Compute(out anchor, out curDir, out width);
            dragging = false;

            List<Pawn> live = LivePawns();
            if (live.Count == 0)
            {
                Close();
                return;
            }
            FormationMemory.lastDef = def;
            FormationMemory.SetWidth(def, width);
            int spacing = FormationMemory.Spacing(def);

            FormationPlan plan = FormationUtility.MakePlan(live, def, width, spacing);
            Regiment reg = GameComponent_Regiments.Get().Assign(live, def, width, spacing, anchor, curDir, FormationMemory.hold);
            FormationUtility.Execute(plan, anchor, curDir, Find.CurrentMap, reg);
            Close();
        }

        void DrawBar(Rect rect)
        {
            Widgets.DrawWindowBackground(rect);
            Rect inner = rect.ContractedBy(6f);

            // Row 1: formation icons, grouped by era
            DrawFormationGrid(new Rect(inner.x, inner.y, inner.width, EraLabelHeight + ButtonSize), defs, ref def, 0);

            // Row 2: name, spacing, hold, roles
            float y = inner.y + EraLabelHeight + ButtonSize + 6f;
            Dialog_FormationInfo.InfoButton(new Rect(inner.x, y + 1f, 24f, 24f), def);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inner.x + 30f, y, 190f, 26f), def.LabelCap);
            int spacing = FormationMemory.Spacing(def);
            float sx = inner.x + 230f;
            Widgets.Label(new Rect(sx, y, 100f, 26f), "BF_Spacing".Translate() + ": " + spacing);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(sx + 100f, y, 28f, 26f), "-"))
                FormationMemory.SetSpacing(def, spacing - 1);
            if (Widgets.ButtonText(new Rect(sx + 132f, y, 28f, 26f), "+"))
                FormationMemory.SetSpacing(def, spacing + 1);

            Rect holdRect = new Rect(sx + 180f, y, 200f, 26f);
            Widgets.CheckboxLabeled(holdRect, "BF_Hold".Translate(), ref FormationMemory.hold);
            TooltipHandler.TipRegion(holdRect, "BF_HoldDesc".Translate());

            if (Widgets.ButtonText(new Rect(inner.xMax - 160f, y, 160f, 26f), "BF_RolesButton".Translate()))
            {
                Close();
                Find.WindowStack.Add(new Dialog_Formation(pawns));
            }
        }

        public const float ButtonSize = 52f;
        public const float ButtonGap = 4f;
        public const float EraGap = 14f;
        public const float EraLabelHeight = 16f;

        // Icon buttons with small era headers (Bannerlord-style order menu).
        // perRow = 0: one row with gaps between eras; otherwise wraps, each era starting a new row.
        public static void DrawFormationGrid(Rect rect, List<FormationDef> defs, ref FormationDef selected, int perRow)
        {
            bool wrap = perRow > 0;
            float startX = rect.x;
            if (!wrap)
            {
                float rowWidth = 0f;
                for (int i = 0; i < defs.Count; i++)
                    rowWidth += ButtonSize + ButtonGap + (i > 0 && defs[i].era != defs[i - 1].era ? EraGap : 0f);
                startX = rect.x + Mathf.Max(0f, (rect.width - rowWidth + ButtonGap) / 2f);
            }

            float x = startX, y = rect.y + EraLabelHeight;
            int col = 0;
            for (int i = 0; i < defs.Count; i++)
            {
                FormationDef d = defs[i];
                bool newEra = i == 0 || d.era != defs[i - 1].era;
                if (i > 0 && wrap && (newEra || col == perRow))
                {
                    x = startX;
                    col = 0;
                    y += ButtonSize + ButtonGap + (newEra ? EraLabelHeight : 0f);
                }
                else if (i > 0 && newEra)
                {
                    x += EraGap;
                }
                col++;
                if (newEra)
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.75f, 0.75f, 0.75f);
                    Widgets.Label(new Rect(x, y - EraLabelHeight, 200f, EraLabelHeight), ("BF_Era_" + d.era).Translate());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                if (FormationButton(new Rect(x, y, ButtonSize, ButtonSize), d, d == selected))
                    selected = d;
                x += ButtonSize + ButtonGap;
            }
        }

        public static bool FormationButton(Rect rect, FormationDef d, bool selected)
        {
            Widgets.DrawOptionBackground(rect, selected);
            if (d.Icon != null)
            {
                GUI.DrawTexture(rect.ContractedBy(3f), d.Icon);
            }
            else
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(rect.ContractedBy(3f), d.LabelCap);
                Text.Font = GameFont.Small;
            }
            TooltipHandler.TipRegion(rect, d.LabelCap.ToString().Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + d.description +
                "\n\n" + Dialog_FormationInfo.TacticText(d));
            return Widgets.ButtonInvisible(rect);
        }
    }
}
