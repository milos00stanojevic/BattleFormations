using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BattleFormations
{
    public class Regiment : IExposable
    {
        public int id;
        public List<Pawn> members = new List<Pawn>();
        public FormationDef def;
        public int width = 8;
        public int spacing = 1;
        public int dir;
        public IntVec3 anchor = IntVec3.Invalid; // front center
        public bool hold = true;
        public bool marching;
        public IntVec3 marchTarget = IntVec3.Invalid;
        public int stepStartTick;

        public Dictionary<Pawn, IntVec3> slots = new Dictionary<Pawn, IntVec3>();  // world cell per member
        public Dictionary<Pawn, IntVec3> locals = new Dictionary<Pawn, IntVec3>(); // formation-local slot per member
        public HashSet<Pawn> detached = new HashSet<Pawn>(); // ordered away alone: left to the player
        public bool active;  // formed up via the formation button; ends when everyone is undrafted
        public bool ambushArmed;
        public int armedTick;
        public List<Pawn> silenced = new List<Pawn>(); // fire at will switched off by the ambush

        List<Pawn> slotKeys, localKeys;
        List<IntVec3> slotValues, localValues;

        public string Label
        {
            get { return "BF_Regiment".Translate() + " " + id; }
        }

        public Map Map
        {
            get
            {
                foreach (Pawn p in members)
                {
                    if (p != null && p.Spawned)
                        return p.Map;
                }
                return null;
            }
        }

        public List<Pawn> LiveDrafted(Map map)
        {
            return members.Where(p => p != null && p.Spawned && p.Map == map && !p.Dead && !p.Downed && p.Drafted).ToList();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Collections.Look(ref members, "members", LookMode.Reference);
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref width, "width", 8);
            Scribe_Values.Look(ref spacing, "spacing", 1);
            Scribe_Values.Look(ref dir, "dir");
            Scribe_Values.Look(ref anchor, "anchor", IntVec3.Invalid);
            Scribe_Values.Look(ref hold, "hold", true);
            Scribe_Values.Look(ref marching, "marching");
            Scribe_Values.Look(ref marchTarget, "marchTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref stepStartTick, "stepStartTick");
            Scribe_Collections.Look(ref slots, "slots", LookMode.Reference, LookMode.Value, ref slotKeys, ref slotValues);
            Scribe_Collections.Look(ref locals, "locals", LookMode.Reference, LookMode.Value, ref localKeys, ref localValues);
            Scribe_Values.Look(ref active, "active");
            Scribe_Values.Look(ref ambushArmed, "ambushArmed");
            Scribe_Values.Look(ref armedTick, "armedTick");
            Scribe_Collections.Look(ref silenced, "silenced", LookMode.Reference);
            Scribe_Collections.Look(ref detached, "detached", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (members == null)
                    members = new List<Pawn>();
                members.RemoveAll(p => p == null);
                if (detached == null)
                    detached = new HashSet<Pawn>();
                detached.RemoveWhere(p => p == null);
                if (slots == null)
                    slots = new Dictionary<Pawn, IntVec3>();
                if (locals == null)
                    locals = new Dictionary<Pawn, IntVec3>();
                if (silenced == null)
                    silenced = new List<Pawn>();
                silenced.RemoveAll(p => p == null);
            }
        }
    }

    // Regiments: created by placing a formation. A normal right-click move order on a holding
    // regiment marches the whole regiment as a block, in short steps that keep everyone in place.
    public class GameComponent_Regiments : GameComponent
    {
        const int StepLength = 8;
        const int StepLookAhead = 3;
        const int StepTimeoutTicks = 600;
        const int HotkeyCount = 5;

        const int TacticInterval = 30;
        const float ChargeRange = 12f;   // enemies this close make a charging regiment attack
        const float SafeRadius = 6f;     // no enemy this close: displaced fighters walk back to their slot
        const float SkirmishRange = 5f;  // melee enemies this close make skirmishers fall back
        const float AmbushRange = 14f;   // enemies this close spring the ambush
        const int ReturnCooldown = 300;
        const int ReliefCooldown = 900;

        public List<Regiment> regiments = new List<Regiment>();
        readonly Dictionary<Pawn, int> handledJob = new Dictionary<Pawn, int>();
        readonly HashSet<int> ownJobs = new HashSet<int>();  // attack jobs given by the tactics, not the player
        readonly Dictionary<Pawn, int> nextReturn = new Dictionary<Pawn, int>();
        readonly Dictionary<Pawn, int> lastRelief = new Dictionary<Pawn, int>();
        KeyBindingDef[] hotkeys;

        public GameComponent_Regiments(Game game)
        {
        }

        public static GameComponent_Regiments Get()
        {
            return Current.Game.GetComponent<GameComponent_Regiments>();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref regiments, "bfRegiments", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (regiments == null)
                    regiments = new List<Regiment>();
                regiments.RemoveAll(r => r == null || r.members.Count == 0);
            }
        }

        // Regiment that contains all given pawns, if any.
        public Regiment RegimentOf(List<Pawn> pawns)
        {
            if (pawns.Count == 0)
                return null;
            foreach (Regiment r in regiments)
            {
                if (pawns.All(p => r.members.Contains(p)))
                    return r;
            }
            return null;
        }

        public Regiment Assign(List<Pawn> pawns, FormationDef def, int width, int spacing, IntVec3 anchor, int dir, bool hold)
        {
            Regiment reg = RegimentOf(pawns);
            if (reg == null || pawns.Count * 2 < reg.members.Count)
            {
                foreach (Regiment r in regiments)
                    r.members.RemoveAll(p => pawns.Contains(p));
                reg = new Regiment();
                reg.id = NextFreeId();
                reg.members.AddRange(pawns);
                regiments.Add(reg);
            }
            reg.def = def;
            reg.width = width;
            reg.spacing = spacing;
            reg.anchor = anchor;
            reg.dir = dir;
            reg.hold = hold;
            reg.marching = false;
            reg.active = true;
            reg.detached.Clear();
            // Members left out of this placement stay in the roster but not in the formation.
            foreach (Pawn p in reg.members)
            {
                if (!pawns.Contains(p))
                    reg.detached.Add(p);
            }
            Disarm(reg);
            if (def.tactic == FormationTactic.Ambush)
            {
                reg.ambushArmed = true;
                reg.armedTick = Find.TickManager.TicksGame;
                foreach (Pawn p in pawns.Where(p => p.drafter != null))
                {
                    p.drafter.FireAtWill = false;
                    reg.silenced.Add(p);
                }
            }
            foreach (Regiment r in regiments.Where(r => r.members.Count == 0).ToList())
                Disarm(r);
            regiments.RemoveAll(r => r.members.Count == 0);
            regiments.SortBy(r => r.id);
            return reg;
        }

        public static void SetHold(Regiment reg, bool hold)
        {
            reg.hold = hold;
            if (!hold)
                reg.marching = false;
        }

        public void Disband(Regiment reg)
        {
            Disarm(reg);
            regiments.Remove(reg);
        }

        int NextFreeId()
        {
            int id = 1;
            while (regiments.Any(r => r.id == id))
                id++;
            return id;
        }

        // Detect fresh player orders for regiment members: move orders move the regiment,
        // a member ordered alone (move or attack) is left to the player until the next placement.
        public override void GameComponentUpdate()
        {
            foreach (Regiment reg in regiments)
            {
                if (reg.def == null)
                    continue;
                Map map = reg.Map;
                if (map == null)
                    continue;
                List<Pawn> live = reg.LiveDrafted(map);
                List<Pawn> movers = new List<Pawn>();
                int sx = 0, sz = 0;
                foreach (Pawn p in live)
                {
                    Job job = p.CurJob;
                    if (job == null || !job.playerForced)
                        continue;
                    int last;
                    if (handledJob.TryGetValue(p, out last) && last == job.loadID)
                        continue;
                    handledJob[p] = job.loadID;
                    if (job.def == BFDefOf.BF_TakeFormationPosition || ownJobs.Contains(job.loadID))
                        continue;
                    if (job.def == JobDefOf.Goto)
                    {
                        movers.Add(p);
                        sx += job.targetA.Cell.x;
                        sz += job.targetA.Cell.z;
                    }
                    else if (job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic)
                    {
                        reg.detached.Add(p);
                    }
                }
                if (movers.Count == 0)
                    continue;
                // The regiment only moves together when all its members still in formation were selected
                // for the order; a part of it ordered away leaves the formation and walks normally.
                List<Pawn> formed = live.Where(p => !reg.detached.Contains(p)).ToList();
                if (formed.Count == 0)
                    formed = live; // everyone was ordered away: moving the whole regiment forms it up again
                if (reg.hold && formed.Count > 0 && formed.All(p => Find.Selector.IsSelected(p)))
                {
                    foreach (Pawn p in movers)
                        reg.detached.Remove(p); // selected stragglers rejoin
                    reg.active = true; // a regiment that was stood down forms up again when moved as a whole
                    MoveRegiment(reg, live.Where(p => !reg.detached.Contains(p)).ToList(), map, new IntVec3(sx / movers.Count, 0, sz / movers.Count));
                }
                else
                    reg.detached.AddRange(movers);
            }
        }

        // The regiment marches as a block toward the clicked cell, in short steps, everyone keeping their
        // place in the formation. The clicked cell is where the middle of the formation ends up.
        void MoveRegiment(Regiment reg, List<Pawn> live, Map map, IntVec3 target)
        {
            IntVec3 center = Centroid(live);
            IntVec3 delta = target - center;
            int dir = delta.LengthHorizontalSquared >= 4 ? FormationUtility.DirFromVector(delta, reg.dir) : reg.dir;
            // Small turns keep everyone's place; after a big turn places are handed out by distance,
            // otherwise the left and right ends would have to cross the whole block.
            int turn = ((dir - reg.dir) % 8 + 8) % 8;
            bool keepPlaces = turn <= 1 || turn >= 7;
            reg.dir = dir;
            IntVec3 offset = CenterOffset(reg, live, dir);
            reg.anchor = center - offset;
            reg.marchTarget = (target - offset).ClampInsideMap(map);
            reg.marching = true;
            NextStep(reg, live, map, keepPlaces);
        }

        void NextStep(Regiment reg, List<Pawn> live, Map map, bool keepPlaces)
        {
            IntVec3 delta = reg.marchTarget - reg.anchor;
            float dist = delta.LengthHorizontal;
            if (dist <= StepLength)
            {
                reg.anchor = reg.marchTarget;
                reg.marching = false;
            }
            else
            {
                reg.anchor += new IntVec3(Mathf.RoundToInt(delta.x / dist * StepLength), 0, Mathf.RoundToInt(delta.z / dist * StepLength));
            }
            FormationPlan plan = FormationUtility.MakePlan(live, reg.def, reg.width, reg.spacing);
            FormationUtility.Execute(plan, reg.anchor, reg.dir, map, reg, keepPlaces);
            reg.stepStartTick = Find.TickManager.TicksGame;
            KeepPace(reg, live);
        }

        // Marching discipline: everyone should reach their next place at the same moment, so members who
        // are faster or have less way to go are slowed down. Only ever slows down, and ends on arrival.
        static void KeepPace(Regiment reg, List<Pawn> live)
        {
            List<Pawn> walking = new List<Pawn>();
            List<float> dists = new List<float>(), speeds = new List<float>();
            float time = 0f;
            foreach (Pawn p in live)
            {
                Hediff old = p.health.hediffSet.GetFirstHediffOfDef(BFDefOf.BF_MarchPace);
                if (old != null)
                    p.health.RemoveHediff(old);
                IntVec3 slot;
                if (p.CurJobDef != BFDefOf.BF_TakeFormationPosition || !reg.slots.TryGetValue(p, out slot))
                    continue;
                float speed = p.GetStatValue(StatDefOf.MoveSpeed);
                if (speed <= 0f)
                    continue;
                float d = p.Position.DistanceTo(slot);
                walking.Add(p);
                dists.Add(d);
                speeds.Add(speed);
                time = Mathf.Max(time, d / speed);
            }
            if (time <= 0f)
                return;
            for (int i = 0; i < walking.Count; i++)
            {
                float slowdown = 1f - dists[i] / (time * speeds[i]);
                if (slowdown < 0.025f)
                    continue;
                Hediff pace = HediffMaker.MakeHediff(BFDefOf.BF_MarchPace, walking[i]);
                pace.Severity = Mathf.Min(slowdown + 0.025f, BFDefOf.BF_MarchPace.maxSeverity);
                walking[i].health.AddHediff(pace);
            }
        }

        // From the front center to the middle of the formation, in world cells for the given facing.
        static IntVec3 CenterOffset(Regiment reg, List<Pawn> live, int dir)
        {
            FormationPlan plan = FormationUtility.MakePlan(live, reg.def, reg.width, reg.spacing);
            if (plan.local.Count == 0)
                return IntVec3.Zero;
            float x = 0f, z = 0f;
            foreach (IntVec3 c in plan.local)
            {
                x += c.x;
                z += c.z;
            }
            IntVec3 mid = new IntVec3(Mathf.RoundToInt(x / plan.local.Count), 0, Mathf.RoundToInt(z / plan.local.Count));
            return FormationUtility.ToWorld(mid, IntVec3.Zero, dir);
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 == 0)
                Cleanup();
            if (now % TacticInterval == 0)
                TickTactics(now);
            if (now % 15 != 0)
                return;

            foreach (Regiment reg in regiments)
            {
                if (!reg.marching)
                    continue;
                Map map = reg.Map;
                List<Pawn> live = map != null ? reg.LiveDrafted(map).Where(p => !reg.detached.Contains(p)).ToList() : null;
                if (live == null || live.Count == 0 || reg.def == null || !reg.active)
                {
                    reg.marching = false;
                    continue;
                }
                bool stepDone = true;
                foreach (Pawn p in live)
                {
                    Job job = p.CurJob;
                    if (job == null)
                        continue;
                    if (job.def == BFDefOf.BF_TakeFormationPosition)
                    {
                        // The next step is handed out shortly before everyone arrives, so the block keeps walking.
                        IntVec3 slot;
                        if (!reg.slots.TryGetValue(p, out slot) || p.Position.DistanceToSquared(slot) > StepLookAhead * StepLookAhead)
                            stepDone = false;
                    }
                    else if (job.playerForced && job.def != JobDefOf.Goto)
                    {
                        // Player gave another order (attack, etc.): stop marching.
                        reg.marching = false;
                        break;
                    }
                }
                if (reg.marching && (stepDone || now - reg.stepStartTick > StepTimeoutTicks))
                    NextStep(reg, live, map, true);
            }
        }

        void Cleanup()
        {
            foreach (Regiment reg in regiments)
            {
                reg.members.RemoveAll(p => p == null || p.Dead || p.Destroyed || p.Faction != Faction.OfPlayer);
                foreach (Pawn p in reg.slots.Keys.Where(p => !reg.members.Contains(p)).ToList())
                {
                    reg.slots.Remove(p);
                    reg.locals.Remove(p);
                }
                reg.detached.RemoveWhere(p => !reg.members.Contains(p));
                if (reg.members.Count == 0)
                    Disarm(reg);
            }
            regiments.RemoveAll(r => r.members.Count == 0);
            foreach (Pawn p in handledJob.Keys.Where(p => p.Destroyed).ToList())
                handledJob.Remove(p);
            foreach (Pawn p in nextReturn.Keys.Where(p => p.Destroyed).ToList())
                nextReturn.Remove(p);
            foreach (Pawn p in lastRelief.Keys.Where(p => p.Destroyed).ToList())
                lastRelief.Remove(p);
            ownJobs.RemoveWhere(id => !regiments.Any(r => r.members.Any(p => p.CurJob != null && p.CurJob.loadID == id)));
        }

        // ---- Tactics: what a regiment does on its own ----

        void TickTactics(int now)
        {
            Dictionary<Map, List<Pawn>> enemiesByMap = new Dictionary<Map, List<Pawn>>();
            foreach (Regiment reg in regiments)
            {
                Map map = reg.Map;
                if (reg.def == null || map == null)
                    continue;
                List<Pawn> enemies;
                if (!enemiesByMap.TryGetValue(map, out enemies))
                {
                    enemies = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer)).ToList();
                    enemiesByMap[map] = enemies;
                }
                List<Pawn> live = reg.LiveDrafted(map);

                // Undrafted members leave the formation; once nobody is left, it is dissolved
                // and the group behaves like normal RimWorld until it is formed up again.
                foreach (Pawn p in reg.members)
                {
                    if (!p.Drafted)
                        reg.detached.Add(p);
                }
                if (live.Count == 0 && reg.active)
                {
                    reg.active = false;
                    reg.marching = false;
                    Disarm(reg);
                }
                if (!reg.active)
                    continue;
                live = live.Where(p => !reg.detached.Contains(p)).ToList();

                TickAmbush(reg, live, enemies);
                switch (reg.def.tactic)
                {
                    case FormationTactic.Charge:
                    case FormationTactic.WingCharge:
                    case FormationTactic.Envelop:
                        Charge(reg, live, enemies);
                        break;
                    case FormationTactic.Relief:
                        Relieve(reg, live, now);
                        break;
                    case FormationTactic.Skirmish:
                        Skirmish(reg, live, enemies, map);
                        break;
                }
                ReturnToSlots(reg, live, enemies, now);
            }
        }

        // Drafted and standing around (not walking, not attacking, not aiming).
        static bool Idle(Pawn p)
        {
            return p.CurJobDef == JobDefOf.Wait_Combat && !(p.stances.curStance is Stance_Busy);
        }

        static Pawn Nearest(IntVec3 from, List<Pawn> enemies, float maxDist, bool meleeOnly)
        {
            Pawn best = null;
            float bestDist = maxDist * maxDist;
            foreach (Pawn e in enemies)
            {
                if (meleeOnly && e.equipment != null && e.equipment.Primary != null && e.equipment.Primary.def.IsRangedWeapon)
                    continue;
                float d = e.Position.DistanceToSquared(from);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            return best;
        }

        // Fighters who were pushed out of place (or charged, or fell back) walk back once it is safe.
        void ReturnToSlots(Regiment reg, List<Pawn> live, List<Pawn> enemies, int now)
        {
            Rot4 facing = FormationUtility.DirToRot(reg.dir);
            foreach (Pawn p in live)
            {
                IntVec3 slot;
                if (reg.detached.Contains(p) || !Idle(p) || !reg.slots.TryGetValue(p, out slot) || p.Position == slot)
                    continue;
                int next;
                if (nextReturn.TryGetValue(p, out next) && now < next)
                    continue;
                if (Nearest(p.Position, enemies, SafeRadius, false) != null || Nearest(slot, enemies, SafeRadius - 2f, false) != null)
                    continue;
                nextReturn[p] = now + ReturnCooldown;
                FormationUtility.OrderTo(p, slot, facing);
            }
        }

        // Charge: melee fighters attack the nearest enemy once one comes close.
        // WingCharge: only the left wing (oblique order). Envelop: only both outer wings (Cannae).
        void Charge(Regiment reg, List<Pawn> live, List<Pawn> enemies)
        {
            if (enemies.Count == 0 || reg.locals.Count == 0 || !live.Any(p => Nearest(p.Position, enemies, ChargeRange, false) != null))
                return;
            int minX = reg.locals.Values.Min(v => v.x), maxX = reg.locals.Values.Max(v => v.x);
            float mid = (minX + maxX) / 2f, third = (maxX - minX) / 3f;
            foreach (Pawn p in live)
            {
                IntVec3 local;
                if (reg.detached.Contains(p) || !Idle(p) || !reg.locals.TryGetValue(p, out local) || FormationUtility.RoleOf(p) == FormationRole.Ranged)
                    continue;
                if (reg.def.tactic == FormationTactic.WingCharge && local.x >= mid)
                    continue;
                if (reg.def.tactic == FormationTactic.Envelop && local.x > minX + third && local.x < maxX - third)
                    continue;
                Pawn target = Nearest(p.Position, enemies, ChargeRange * 2f, false);
                if (target == null)
                    continue;
                Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                ownJobs.Add(job.loadID);
                p.jobs.TryTakeOrderedJob(job, JobTag.DraftedOrder, false);
            }
        }

        // Line relief: a wounded fighter in the front rank swaps places with a healthy one from behind.
        void Relieve(Regiment reg, List<Pawn> live, int now)
        {
            if (reg.locals.Count == 0)
                return;
            int frontZ = reg.locals.Values.Max(v => v.z);
            Rot4 facing = FormationUtility.DirToRot(reg.dir);
            foreach (Pawn hurt in live)
            {
                IntVec3 hl;
                if (!reg.locals.TryGetValue(hurt, out hl) || hl.z < frontZ - reg.spacing || !Hurt(hurt) || !CanRelieve(reg, hurt, now))
                    continue;
                FormationRole role = FormationUtility.RoleOf(hurt);
                Pawn fresh = live
                    .Where(q => q != hurt && reg.locals.ContainsKey(q) && reg.locals[q].z < frontZ - reg.spacing && Healthy(q) && CanRelieve(reg, q, now))
                    .Where(q => FormationUtility.RoleOf(q) != FormationRole.Ranged || role == FormationRole.Ranged)
                    .OrderBy(q => FormationUtility.RoleOf(q) == role ? 0 : 1)
                    .ThenBy(q => q.Position.DistanceToSquared(hurt.Position))
                    .FirstOrDefault();
                if (fresh == null)
                    continue;

                IntVec3 slot = reg.slots[hurt];
                reg.slots[hurt] = reg.slots[fresh];
                reg.slots[fresh] = slot;
                IntVec3 local = reg.locals[hurt];
                reg.locals[hurt] = reg.locals[fresh];
                reg.locals[fresh] = local;
                lastRelief[hurt] = now;
                lastRelief[fresh] = now;
                FormationUtility.OrderTo(fresh, reg.slots[fresh], facing);
                FormationUtility.OrderTo(hurt, reg.slots[hurt], facing);
            }
        }

        bool CanRelieve(Regiment reg, Pawn p, int now)
        {
            int last;
            if (lastRelief.TryGetValue(p, out last) && now - last < ReliefCooldown)
                return false;
            return !reg.detached.Contains(p) && reg.slots.ContainsKey(p) && p.CurJobDef == JobDefOf.Wait_Combat;
        }

        static bool Hurt(Pawn p)
        {
            return p.health.summaryHealth.SummaryHealthPercent < 0.7f || p.health.hediffSet.BleedRateTotal > 0.1f || p.health.hediffSet.PainTotal > 0.3f;
        }

        static bool Healthy(Pawn p)
        {
            return p.health.summaryHealth.SummaryHealthPercent > 0.9f && p.health.hediffSet.BleedRateTotal <= 0f && p.health.hediffSet.PainTotal < 0.15f;
        }

        // Skirmish: ranged fighters fall back from melee enemies and come back later (ReturnToSlots).
        void Skirmish(Regiment reg, List<Pawn> live, List<Pawn> enemies, Map map)
        {
            foreach (Pawn p in live)
            {
                if (reg.detached.Contains(p) || p.CurJobDef != JobDefOf.Wait_Combat || FormationUtility.RoleOf(p) != FormationRole.Ranged)
                    continue;
                Pawn threat = Nearest(p.Position, enemies, SkirmishRange, true);
                if (threat == null)
                    continue;
                IntVec3 away = p.Position - threat.Position;
                float len = Mathf.Max(1f, away.LengthHorizontal);
                IntVec3 cell = p.Position + new IntVec3(Mathf.RoundToInt(away.x / len * 7f), 0, Mathf.RoundToInt(away.z / len * 7f));
                IntVec3 dest = RCellFinder.BestOrderedGotoDestNear(cell.ClampInsideMap(map), p, null, false);
                if (!dest.IsValid || dest == p.Position)
                    continue;
                FormationUtility.OrderTo(p, dest, FormationUtility.DirToRot(FormationUtility.DirFromVector(threat.Position - dest, reg.dir)));
            }
        }

        // Ambush: fire at will stays off until an enemy comes close, someone is hit,
        // or the player switches fire at will back on for any member.
        void TickAmbush(Regiment reg, List<Pawn> live, List<Pawn> enemies)
        {
            if (!reg.ambushArmed)
                return;
            foreach (Pawn p in reg.silenced.ToList())
            {
                if (!live.Contains(p))
                {
                    Release(p);
                    reg.silenced.Remove(p);
                }
            }
            bool spring = false;
            foreach (Pawn p in live)
            {
                if (p.drafter == null)
                    continue;
                if (!reg.silenced.Contains(p))
                {
                    p.drafter.FireAtWill = false;
                    reg.silenced.Add(p);
                    continue;
                }
                if (p.drafter.FireAtWill || p.mindState.lastHarmTick > reg.armedTick || Nearest(p.Position, enemies, AmbushRange, false) != null)
                    spring = true;
            }
            if (!spring)
                return;
            Disarm(reg);
            Messages.Message("BF_AmbushSprung".Translate(reg.Label), live.Count > 0 ? new LookTargets(live[0]) : LookTargets.Invalid,
                MessageTypeDefOf.NeutralEvent, false);
        }

        static void Disarm(Regiment reg)
        {
            foreach (Pawn p in reg.silenced)
                Release(p);
            reg.silenced.Clear();
            reg.ambushArmed = false;
        }

        static void Release(Pawn p)
        {
            if (p != null && p.drafter != null)
                p.drafter.FireAtWill = true;
        }

        static IntVec3 Centroid(List<Pawn> pawns)
        {
            return new IntVec3(
                Mathf.RoundToInt((float)pawns.Average(p => p.Position.x)), 0,
                Mathf.RoundToInt((float)pawns.Average(p => p.Position.z)));
        }

        // ---- UI: regiment bar below the colonist bar + hotkeys ----

        public override void GameComponentOnGUI()
        {
            Map map = Find.CurrentMap;
            if (map == null || WorldRendererUtility.WorldRendered || regiments.Count == 0)
                return;

            if (hotkeys == null)
            {
                hotkeys = new KeyBindingDef[HotkeyCount];
                for (int i = 0; i < HotkeyCount; i++)
                    hotkeys[i] = DefDatabase<KeyBindingDef>.GetNamedSilentFail("BF_Regiment" + (i + 1));
            }
            for (int i = 0; i < HotkeyCount; i++)
            {
                if (hotkeys[i] == null || !hotkeys[i].KeyDownEvent)
                    continue;
                Regiment r = regiments.FirstOrDefault(x => x.id == i + 1);
                if (r != null)
                {
                    SelectRegiment(r);
                    Event.current.Use();
                }
            }

            DrawBar(map);
        }

        void DrawBar(Map map)
        {
            List<Regiment> shown = regiments.Where(r => r.Map == map).ToList();
            if (shown.Count == 0)
                return;

            const float w = 120f, h = 26f, gap = 4f;
            float x = (UI.screenWidth - (shown.Count * (w + gap) - gap)) / 2f;
            float y = 8f;
            ColonistBar bar = Find.ColonistBar;
            if (bar != null && bar.DrawLocs.Count > 0)
                y = bar.DrawLocs.Max(v => v.y) + bar.Size.y + 22f;

            foreach (Regiment r in shown)
            {
                Rect rect = new Rect(x, y, w, h);
                Event ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 1 && Mouse.IsOver(rect))
                {
                    Find.WindowStack.Add(new FloatMenu(Options(r)));
                    ev.Use();
                }

                bool selected = r.members.Any(p => Find.Selector.IsSelected(p));
                if (selected)
                    GUI.color = new Color(1f, 0.85f, 0.45f);
                string label = r.Label + (r.hold ? "" : " *");
                if (Widgets.ButtonText(rect, label))
                    SelectRegiment(r);
                GUI.color = Color.white;

                string key = r.id <= HotkeyCount && hotkeys != null && hotkeys[r.id - 1] != null
                    ? hotkeys[r.id - 1].MainKeyLabel : "-";
                TooltipHandler.TipRegion(rect, (r.def != null ? r.def.LabelCap.ToString() : "") + "\n" +
                    "BF_RegimentTip".Translate() + " " + key + "\n" + (r.hold ? "BF_HoldOn" : "BF_HoldOff").Translate());
                x += w + gap;
            }
        }

        static void SelectRegiment(Regiment r)
        {
            Map map = Find.CurrentMap;
            List<Pawn> pawns = r.members.Where(p => p.Spawned && p.Map == map).ToList();
            if (pawns.Count == 0)
                return;
            bool already = Find.Selector.NumSelected == pawns.Count && pawns.All(p => Find.Selector.IsSelected(p));
            if (already)
            {
                Find.CameraDriver.JumpToCurrentMapLoc(Centroid(pawns));
                return;
            }
            Find.Selector.ClearSelection();
            foreach (Pawn p in pawns)
                Find.Selector.Select(p, false, false);
        }

        List<FloatMenuOption> Options(Regiment r)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("BF_DraftAll".Translate(), delegate
            {
                foreach (Pawn p in r.members)
                {
                    if (p.Spawned && p.drafter != null && !p.Downed)
                        p.drafter.Drafted = true;
                }
            }));
            options.Add(new FloatMenuOption((r.hold ? "BF_SetHoldOff" : "BF_SetHoldOn").Translate(), delegate
            {
                SetHold(r, !r.hold);
            }));
            options.Add(new FloatMenuOption("BF_Disband".Translate(), delegate { Disband(r); }));
            return options;
        }
    }
}
