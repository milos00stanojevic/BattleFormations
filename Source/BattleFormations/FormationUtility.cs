using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BattleFormations
{
    public class FormationPlan
    {
        public FormationDef def;
        public List<Pawn> pawns;          // sorted front to back by role rank
        public List<FormationRole> roles; // role per pawn, same order
        public List<IntVec3> local;       // x = right, z = forward; front center = (0,0)
    }

    public static class FormationUtility
    {
        static readonly string[] PikeWords =
        {
            "spear", "pike", "lance", "halberd", "glaive", "polearm", "sarissa", "pilum",
            "trident", "partisan", "bardiche", "naginata", "speer", "lanze", "hellebarde"
        };

        public static List<Pawn> SelectedFormationPawns()
        {
            List<Pawn> result = new List<Pawn>();
            foreach (Pawn p in Find.Selector.SelectedPawns)
            {
                if (p.Spawned && p.Drafted && p.IsColonistPlayerControlled && p.Map == Find.CurrentMap)
                    result.Add(p);
            }
            return result;
        }

        public static FormationRole AutoRole(Pawn pawn)
        {
            ThingWithComps weapon = pawn.equipment != null ? pawn.equipment.Primary : null;
            if (weapon == null)
                return FormationRole.Melee;
            if (weapon.def.IsRangedWeapon)
                return FormationRole.Ranged;
            string text = (weapon.def.defName + " " + weapon.def.label).ToLowerInvariant();
            foreach (string word in PikeWords)
            {
                if (text.Contains(word))
                    return FormationRole.Pike;
            }
            return FormationRole.Melee;
        }

        public static FormationRole RoleOf(Pawn pawn)
        {
            CompFormation comp = pawn.GetComp<CompFormation>();
            if (comp != null && comp.roleOverride != RoleOverride.Auto)
                return (FormationRole)((int)comp.roleOverride - 1);
            return AutoRole(pawn);
        }

        public static string RoleLabel(FormationRole role)
        {
            return ("BF_Role_" + role).Translate();
        }

        public static Color RoleColor(FormationRole role)
        {
            switch (role)
            {
                case FormationRole.Pike: return new Color(0.95f, 0.75f, 0.2f);
                case FormationRole.Ranged: return new Color(0.4f, 0.85f, 0.4f);
                default: return new Color(0.9f, 0.3f, 0.25f);
            }
        }

        public static FormationPlan MakePlan(List<Pawn> pawns, FormationDef def, int width, int spacing)
        {
            FormationPlan plan = new FormationPlan();
            plan.def = def;
            plan.pawns = pawns.OrderBy(p => def.RankOf(RoleOf(p))).ThenBy(p => p.thingIDNumber).ToList();
            plan.roles = plan.pawns.Select(p => RoleOf(p)).ToList();
            plan.local = FormationLayout.Build(def, plan.roles, width, spacing);
            return plan;
        }

        // 8 directions, clockwise from north. Diagonals use (1,1) steps so ranks stay on whole cells.
        static readonly IntVec3[] Dirs =
        {
            new IntVec3(0, 0, 1), new IntVec3(1, 0, 1), new IntVec3(1, 0, 0), new IntVec3(1, 0, -1),
            new IntVec3(0, 0, -1), new IntVec3(-1, 0, -1), new IntVec3(-1, 0, 0), new IntVec3(-1, 0, 1)
        };

        public static IntVec3 DirVector(int dir)
        {
            return Dirs[((dir % 8) + 8) % 8];
        }

        public static int DirFromVector(IntVec3 delta, int fallback)
        {
            if (delta.x == 0 && delta.z == 0)
                return fallback;
            float angle = new Vector3(delta.x, 0f, delta.z).AngleFlat();
            return ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;
        }

        public static Rot4 DirToRot(int dir)
        {
            return Rot4.FromAngleFlat(dir * 45f);
        }

        public static IntVec3 ToWorld(IntVec3 local, IntVec3 anchor, int dir)
        {
            return anchor + DirVector(dir + 2) * local.x + DirVector(dir) * local.z;
        }

        // Sends everyone to their slot. If reg is given, the slots are remembered there.
        // keepPlaces: members keep their previous place in the formation where it still exists.
        public static void Execute(FormationPlan plan, IntVec3 anchor, int dir, Map map, Regiment reg, bool keepPlaces = false)
        {
            HashSet<IntVec3> taken = new HashSet<IntVec3>();
            Dictionary<Pawn, IntVec3> previous = keepPlaces && reg != null ? new Dictionary<Pawn, IntVec3>(reg.locals) : null;
            if (reg != null)
            {
                reg.slots.Clear();
                reg.locals.Clear();
            }
            int n = plan.pawns.Count;
            int i = 0;
            while (i < n)
            {
                // Within one role group, give each slot (front first) to the nearest pawn of that group.
                int j = i;
                while (j < n && plan.roles[j] == plan.roles[i])
                    j++;
                List<Pawn> group = plan.pawns.GetRange(i, j - i)
                    .Where(p => p.Spawned && p.Drafted && p.Map == map).ToList();
                for (int s = i; s < j && group.Count > 0; s++)
                {
                    IntVec3 cell = ToWorld(plan.local[s], anchor, dir);
                    Pawn best = previous != null ? PreviousHolder(group, previous, plan.local[s]) : null;
                    if (best == null)
                        best = group.MinBy(p => p.Position.DistanceToSquared(cell));
                    group.Remove(best);
                    IntVec3 dest = OrderToCell(best, cell, dir, map, taken);
                    if (reg != null && dest.IsValid)
                    {
                        reg.slots[best] = dest;
                        reg.locals[best] = plan.local[s];
                    }
                }
                i = j;
            }
        }

        static Pawn PreviousHolder(List<Pawn> group, Dictionary<Pawn, IntVec3> previous, IntVec3 local)
        {
            foreach (Pawn p in group)
            {
                IntVec3 v;
                if (previous.TryGetValue(p, out v) && v == local)
                    return p;
            }
            return null;
        }

        static IntVec3 OrderToCell(Pawn pawn, IntVec3 cell, int dir, Map map, HashSet<IntVec3> taken)
        {
            IntVec3 dest = cell;
            if (!dest.InBounds(map) || !dest.Standable(map) || taken.Contains(dest))
            {
                dest = FallbackCell(pawn, cell, dir, map, taken);
                if (!dest.IsValid)
                {
                    Predicate<IntVec3> free = c => !taken.Contains(c);
                    dest = RCellFinder.BestOrderedGotoDestNear(cell.ClampInsideMap(map), pawn, free, true);
                }
            }
            if (!dest.IsValid)
                return dest;
            taken.Add(dest);
            OrderTo(pawn, dest, DirToRot(dir));
            return dest;
        }

        // Nearest free cell in the same rank or behind it, so nobody ends up in front of the line.
        static IntVec3 FallbackCell(Pawn pawn, IntVec3 cell, int dir, Map map, HashSet<IntVec3> taken)
        {
            IntVec3 fwd = DirVector(dir);
            int count = GenRadial.NumCellsInRadius(6f);
            for (int k = 1; k < count; k++)
            {
                IntVec3 c = cell + GenRadial.RadialPattern[k];
                IntVec3 d = c - cell;
                if (d.x * fwd.x + d.z * fwd.z > 0)
                    continue;
                if (c.InBounds(map) && c.Standable(map) && !taken.Contains(c)
                    && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                    return c;
            }
            return IntVec3.Invalid;
        }

        // Walk to a cell and face a direction (the formation job).
        public static void OrderTo(Pawn pawn, IntVec3 dest, Rot4 facing)
        {
            Job job = JobMaker.MakeJob(BFDefOf.BF_TakeFormationPosition, dest);
            job.overrideFacing = facing;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.DraftedOrder, false);
        }
    }
}
