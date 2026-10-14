using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace BattleFormations
{
    // Builds local slot offsets (x = right, z = forward, front center at 0,0) for role-sorted pawns.
    public static class FormationLayout
    {
        public static List<IntVec3> Build(FormationDef def, List<FormationRole> roles, int width, int spacing)
        {
            bool stagger = false;
            if (def.fixedRanks > 0)
                width = Mathf.CeilToInt(roles.Count / (float)def.fixedRanks);
            if (def.fixedRanks == 1 && width > MaxLine)
            {
                // A single line would run off the map: wrap into staggered ranks.
                width = MaxLine;
                stagger = true;
            }
            if (def.square)
                width = Mathf.CeilToInt(Mathf.Sqrt(def.shape == FormationShape.Tercio ? roles.Count - RearCount(roles) : roles.Count));
            width = Mathf.Max(1, width);
            spacing = Mathf.Max(1, spacing);
            List<IntVec3> result;
            switch (def.shape)
            {
                case FormationShape.Wedge: result = Wedge(roles, width, spacing); break;
                case FormationShape.Ring: result = Ring(roles, width, spacing); break;
                case FormationShape.Circle: result = Circle(roles, width, spacing); break;
                case FormationShape.Checker: result = Checker(roles.Count, width, spacing); break;
                case FormationShape.Flanks: result = Flanks(roles, width, spacing); break;
                case FormationShape.Tercio: result = Tercio(roles, width, spacing); break;
                case FormationShape.LShape: result = LShape(roles, width, spacing); break;
                default: result = Block(roles, width, spacing, def.rankGap, stagger, def.fixedRanks); break;
            }
            if (def.echelon != 0f || def.curve != 0f)
                Bend(result, def.echelon, def.curve);
            return result;
        }

        static int CenteredX(int col, int rowCount, int spacing)
        {
            return Mathf.FloorToInt((2 * col - (rowCount - 1)) * spacing / 2f);
        }

        // Rearmost role goes inside (ring/circle) or on the wings (flanks), if there is more than one role.
        static int RearCount(List<FormationRole> roles)
        {
            if (roles.Count == 0 || roles.Distinct().Count() < 2)
                return 0;
            FormationRole last = roles[roles.Count - 1];
            return roles.Count(r => r == last);
        }

        const int MaxLine = 15;

        static List<IntVec3> Block(List<FormationRole> roles, int width, int spacing, int rankGap, bool stagger = false, int maxRanks = 0)
        {
            List<IntVec3> result = new List<IntVec3>(new IntVec3[roles.Count]);
            List<List<int>> ranks = Ranks(roles, width, maxRanks);
            for (int row = 0; row < ranks.Count; row++)
            {
                List<int> rank = ranks[row];
                int shift = stagger && row % 2 == 1 ? (spacing + 1) / 2 : 0;
                // In a shared rank the earlier role takes the middle and the later one both ends.
                List<int> cols = Enumerable.Range(0, rank.Count).ToList();
                if (rank.Select(i => roles[i]).Distinct().Count() > 1)
                    cols = cols.OrderBy(c => Mathf.Abs(2 * c - (rank.Count - 1))).ThenBy(c => c).ToList();
                for (int k = 0; k < rank.Count; k++)
                    result[rank[k]] = new IntVec3(CenteredX(cols[k], rank.Count, spacing) + shift, 0, -row * (spacing + rankGap));
            }
            return result;
        }

        // Each role starts its own rank. With a fixed number of ranks the line may widen up to MaxLine for that;
        // if it still doesn't fit, pike and melee share ranks, and as a last resort the ranks are filled in order.
        static List<List<int>> Ranks(List<FormationRole> roles, int width, int maxRanks)
        {
            if (maxRanks <= 0)
                return SplitRanks(roles, width, r => (int)r);
            int limit = Mathf.Max(width, MaxLine);
            for (int w = width; w <= limit; w++)
            {
                List<List<int>> ranks = SplitRanks(roles, w, r => (int)r);
                if (ranks.Count <= maxRanks)
                    return ranks;
            }
            for (int w = width; w <= limit; w++)
            {
                List<List<int>> ranks = SplitRanks(roles, w, r => r == FormationRole.Ranged ? 1 : 0);
                if (ranks.Count <= maxRanks)
                    return ranks;
            }
            return SplitRanks(roles, width, r => 0);
        }

        static List<List<int>> SplitRanks(List<FormationRole> roles, int width, System.Func<FormationRole, int> group)
        {
            List<List<int>> ranks = new List<List<int>>();
            for (int i = 0; i < roles.Count; i++)
            {
                List<int> last = ranks.Count > 0 ? ranks[ranks.Count - 1] : null;
                if (last == null || last.Count >= width || group(roles[last[0]]) != group(roles[i]))
                {
                    last = new List<int>();
                    ranks.Add(last);
                }
                last.Add(i);
            }
            return ranks;
        }

        static List<IntVec3> Wedge(List<FormationRole> roles, int width, int spacing)
        {
            int n = roles.Count;
            List<IntVec3> result = new List<IntVec3>();
            int row = 0;
            while (result.Count < n)
            {
                int cap = Mathf.Min(1 + 2 * row, width);
                int start = result.Count;
                int rowCount = Mathf.Min(cap, n - start);
                // Shared row: the earlier role in the middle, the later one on both ends.
                List<int> cols = Enumerable.Range(0, rowCount).ToList();
                if (roles.GetRange(start, rowCount).Distinct().Count() > 1)
                    cols = cols.OrderBy(c => Mathf.Abs(2 * c - (rowCount - 1))).ThenBy(c => c).ToList();
                for (int k = 0; k < rowCount; k++)
                    result.Add(new IntVec3(CenteredX(cols[k], rowCount, spacing), 0, -row * spacing));
                row++;
            }
            return result;
        }

        // Hollow square: front roles on the edge, the rearmost role inside.
        static List<IntVec3> Ring(List<FormationRole> roles, int width, int spacing)
        {
            int n = roles.Count;
            int inner = RearCount(roles);
            int outer = n - inner;

            // Smallest square that holds the front roles on the edge and the rest inside.
            int side = 2;
            while (4 * (side - 1) < outer || (side - 2) * (side - 2) < inner)
                side++;

            List<IntVec3> perimeter = new List<IntVec3>();
            for (int x = 0; x < side; x++)
                for (int z = 0; z < side; z++)
                    if (x == 0 || z == 0 || x == side - 1 || z == side - 1)
                        perimeter.Add(new IntVec3(x, 0, z));

            // Fill mirror-symmetric to the front axis: front edge from the center out, then the flanks
            // front to back in left/right pairs, then the back edge from the corners in.
            float c = (side - 1) / 2f;
            List<IntVec3> grid = perimeter
                .OrderByDescending(v => v.z)
                .ThenBy(v => v.z == 0 ? -Mathf.Abs(v.x - c) : Mathf.Abs(v.x - c))
                .ThenBy(v => v.x)
                .Take(outer)
                .ToList();

            List<IntVec3> innerCells = new List<IntVec3>();
            for (int x = 1; x < side - 1; x++)
                for (int z = 1; z < side - 1; z++)
                    innerCells.Add(new IntVec3(x, 0, z));
            innerCells = innerCells
                .OrderBy(v => (v.x - c) * (v.x - c) + (v.z - c) * (v.z - c))
                .ThenByDescending(v => v.z)
                .ThenBy(v => Mathf.Abs(v.x - c))
                .ThenBy(v => v.x)
                .ToList();

            grid.AddRange(innerCells.Take(inner));

            List<IntVec3> result = new List<IntVec3>();
            foreach (IntVec3 g in grid)
            {
                int x = Mathf.FloorToInt((g.x - c) * spacing);
                int z = (g.z - (side - 1)) * spacing;
                result.Add(new IntVec3(x, 0, z));
            }
            return result;
        }

        // Orbis: front roles on a circle (front point at 0,0), the rearmost role inside.
        static List<IntVec3> Circle(List<FormationRole> roles, int width, int spacing)
        {
            int n = roles.Count;
            int inner = RearCount(roles);
            int outer = n - inner;
            List<IntVec3> result = new List<IntVec3>();
            if (outer == 0)
                return result;

            float radius = Mathf.Max(1.5f, Mathf.Max(width / 2f, outer * spacing * 1.15f / (2f * Mathf.PI)));
            HashSet<IntVec3> used = new HashSet<IntVec3>();
            int cz = 0;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                result.Clear();
                used.Clear();
                cz = -Mathf.RoundToInt(radius);
                for (int k = 0; k < outer; k++)
                {
                    // Compute one half and mirror it, so rounding can't make the ring lopsided.
                    int kk = k <= outer / 2 ? k : outer - k;
                    float a = 2f * Mathf.PI * kk / outer;
                    int x = Mathf.RoundToInt(Mathf.Sin(a) * radius);
                    IntVec3 v = new IntVec3(k <= outer / 2 ? x : -x, 0, cz + Mathf.RoundToInt(Mathf.Cos(a) * radius));
                    if (!used.Add(v))
                        break;
                    result.Add(v);
                }
                if (result.Count == outer)
                    break;
                radius += 0.5f;
            }

            List<IntVec3> innerCells = new List<IntVec3>();
            int r = Mathf.CeilToInt(radius);
            for (int x = -r; x <= r; x += spacing)
            {
                for (int z = cz - r; z <= cz + r; z += spacing)
                {
                    IntVec3 v = new IntVec3(x, 0, z);
                    float d = Mathf.Sqrt(x * x + (z - cz) * (z - cz));
                    if (d <= radius - 1.2f && !used.Contains(v))
                        innerCells.Add(v);
                }
            }
            innerCells = innerCells
                .OrderBy(v => v.x * v.x + (v.z - cz) * (v.z - cz))
                .ThenByDescending(v => v.z)
                .ThenBy(v => Mathf.Abs(v.x))
                .ThenBy(v => v.x)
                .ToList();
            for (int k = 0; k < inner; k++)
            {
                if (k < innerCells.Count)
                    result.Add(innerCells[k]);
                else
                    result.Add(new IntVec3(CenteredX(k % 5, 5, spacing), 0, cz - r - 1 - (k / 5) * spacing));
            }
            return result;
        }

        // Manipular quincunx: 2x2 units in a checkerboard, gaps as wide as a unit.
        static List<IntVec3> Checker(int n, int width, int spacing)
        {
            int perRow = Mathf.Max(1, width / 2);
            List<IntVec3> result = new List<IntVec3>();
            for (int i = 0; i < n; i++)
            {
                int unit = i / 4, k = i % 4;
                int ur = unit / perRow, uc = unit % perRow;
                int x = uc * 4 + (ur % 2) * 2 + (k % 2);
                int z = -(ur * 3 + k / 2);
                result.Add(new IntVec3(x * spacing, 0, z * spacing));
            }
            int minX = result.Count > 0 ? result.Min(v => v.x) : 0;
            int maxX = result.Count > 0 ? result.Max(v => v.x) : 0;
            int shift = Mathf.FloorToInt((minX + maxX) / 2f);
            for (int i = 0; i < result.Count; i++)
                result[i] = new IntVec3(result[i].x - shift, 0, result[i].z);
            return result;
        }

        // Herse: everyone but the rearmost role in a center block, the rearmost role on both wings, angled forward.
        static List<IntVec3> Flanks(List<FormationRole> roles, int width, int spacing)
        {
            int n = roles.Count;
            int wings = RearCount(roles);
            int center = n - wings;
            if (wings == 0 || center == 0)
                return Block(roles, width, spacing, 0);

            List<IntVec3> result = Block(roles.GetRange(0, center), width, spacing, 0);
            int half = result.Max(v => Mathf.Abs(v.x));
            for (int k = 0; k < wings; k++)
            {
                int side = k % 2 == 0 ? -1 : 1;
                int idx = k / 2;
                int col = idx / 2, row = idx % 2;
                int x = side * (half + 2 * spacing + col * spacing);
                int z = (col + 1) / 2 - row * spacing;
                result.Add(new IntVec3(x, 0, z));
            }
            return result;
        }

        // Tercio: everyone but the rearmost role in a center block, the rearmost role split into four
        // "sleeves" off the corners (front-left, front-right, back-left, back-right, mirror-symmetric).
        static List<IntVec3> Tercio(List<FormationRole> roles, int width, int spacing)
        {
            int n = roles.Count;
            int rear = RearCount(roles);
            int center = n - rear;
            if (rear == 0 || center == 0)
                return Block(roles, width, spacing, 0);

            List<IntVec3> result = Block(roles.GetRange(0, center), width, spacing, 0);
            int minX = result.Min(v => v.x), maxX = result.Max(v => v.x);
            int back = result.Min(v => v.z);
            int sleeveWidth = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.CeilToInt(rear / 4f))));
            for (int k = 0; k < rear; k++)
            {
                int sleeve = k % 4;
                int j = k / 4;
                int col = j % sleeveWidth, row = j / sleeveWidth;
                int x = sleeve % 2 == 0 ? minX - spacing * (1 + col) : maxX + spacing * (1 + col);
                int z = sleeve < 2 ? spacing * (1 + row) : back - spacing * (1 + row);
                result.Add(new IntVec3(x, 0, z));
            }
            return result;
        }

        // L-ambush: long line, the rearmost role (or a third if all the same) as a short arm
        // reaching forward from the right end, so the zone in front is covered from two sides.
        static List<IntVec3> LShape(List<FormationRole> roles, int width, int spacing)
        {
            int n = roles.Count;
            int arm = RearCount(roles);
            if (arm == 0)
                arm = n / 3;
            int line = n - arm;
            if (line == 0)
                return Block(roles, width, spacing, 0);

            List<IntVec3> result = Block(roles.GetRange(0, line), width, spacing, 0);
            int maxX = result.Max(v => v.x);
            // The arm reaches at most half the line length forward; extra fighters stand behind it.
            int maxRows = Mathf.Max(1, Mathf.Min(width, line) / 2);
            int cols = Mathf.Max(arm > 6 ? 2 : 1, Mathf.CeilToInt(arm / (float)maxRows));
            for (int k = 0; k < arm; k++)
                result.Add(new IntVec3(maxX + spacing * (1 + k % cols), 0, spacing * (1 + k / cols)));
            return result;
        }

        // Oblique step-back and crescent bulge, applied on top of any shape.
        static void Bend(List<IntVec3> cells, float echelon, float curve)
        {
            if (cells.Count == 0)
                return;
            int minX = cells.Min(v => v.x), maxX = cells.Max(v => v.x);
            float mid = (minX + maxX) / 2f;
            float half = Mathf.Max(1f, (maxX - minX) / 2f);
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 v = cells[i];
                float t = (v.x - mid) / half;
                int dz = -Mathf.RoundToInt(echelon * (v.x - minX)) + Mathf.RoundToInt(curve * (1f - t * t));
                cells[i] = new IntVec3(v.x, 0, v.z + dz);
            }
        }
    }
}
