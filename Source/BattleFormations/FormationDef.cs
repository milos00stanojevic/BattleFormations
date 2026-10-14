using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace BattleFormations
{
    public enum FormationShape { Block, Wedge, Ring, Circle, Checker, Flanks, Tercio, LShape }

    public enum FormationEra { Antiquity, Medieval, General }

    public enum FormationRole { Pike, Melee, Ranged }

    public enum RoleOverride { Auto, Pike, Melee, Ranged }

    // What a placed regiment does on its own (see GameComponent_Regiments.TickTactics).
    public enum FormationTactic { Hold, Relief, Charge, WingCharge, Envelop, Skirmish, Ambush }

    public class FormationDef : Def
    {
        public FormationShape shape = FormationShape.Block;
        public List<FormationRole> rankOrder = new List<FormationRole>();
        public int defaultWidth = 8;
        public int defaultSpacing = 1;
        public int order;

        public FormationEra era = FormationEra.General;
        public int fixedRanks;   // > 0: number of ranks is fixed, width follows from the pawn count
        public int rankGap;      // extra empty cells between ranks (e.g. triplex acies)
        public float echelon;    // cells each column steps back from left to right (oblique order)
        public float curve;      // > 0: center bulges forward (crescent)
        public bool square;      // width follows the pawn count so the (center) block stays square
        public FormationTactic tactic = FormationTactic.Hold;
        public string iconPath;  // menu icon
        public List<int> iconSample; // icon generator only: sample army as pike, melee, ranged counts

        // Info card (historical background)
        public string origin;
        public string period;
        public string battles;
        public string history;

        [Unsaved]
        Texture2D icon;

        public Texture2D Icon
        {
            get
            {
                if (icon == null && !iconPath.NullOrEmpty())
                    icon = ContentFinder<Texture2D>.Get(iconPath, false);
                return icon;
            }
        }

        // Lower rank = further to the front.
        public int RankOf(FormationRole role)
        {
            int i = rankOrder.IndexOf(role);
            return i < 0 ? rankOrder.Count + (int)role : i;
        }
    }

    [DefOf]
    public static class BFDefOf
    {
        public static JobDef BF_TakeFormationPosition;
        public static HediffDef BF_MarchPace;

        static BFDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BFDefOf));
        }
    }
}
