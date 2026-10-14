// Dev tool: renders the formation menu icons from Defs/FormationDefs.xml using the mod's own FormationLayout.
// Usage: IconGen.exe <RimWorld Managed dir> <FormationDefs.xml> <output dir> [preview sheet path]
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;

namespace BattleFormations.IconGen
{
    public static class Program
    {
        static string managedDir;

        public static int Main(string[] args)
        {
            managedDir = args[0];
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                string path = Path.Combine(managedDir, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Run(args[1], args[2], args.Length > 3 ? args[3] : null);
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run(string defsPath, string outDir, string sheetPath)
        {
            Directory.CreateDirectory(outDir);
            List<string> written = new List<string>();
            XmlDocument doc = new XmlDocument();
            doc.Load(defsPath);
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.Name != "BattleFormations.FormationDef" || node.Attributes["Abstract"] != null)
                    continue;
                FormationDef def = ReadDef(node);
                string file = Path.Combine(outDir, def.iconPath.Substring(def.iconPath.LastIndexOf('/') + 1) + ".png");
                Icons.Draw(def, file);
                written.Add(file);
                Console.WriteLine("icon: " + file);
            }
            if (sheetPath != null)
                Icons.Sheet(written, sheetPath);
        }

        static FormationDef ReadDef(XmlNode node)
        {
            FormationDef def = new FormationDef();
            def.defaultSpacing = 1;
            foreach (XmlNode f in node.ChildNodes)
            {
                switch (f.Name)
                {
                    case "defName": def.defName = f.InnerText; break;
                    case "iconPath": def.iconPath = f.InnerText; break;
                    case "shape": def.shape = (FormationShape)Enum.Parse(typeof(FormationShape), f.InnerText); break;
                    case "defaultWidth": def.defaultWidth = int.Parse(f.InnerText); break;
                    case "defaultSpacing": def.defaultSpacing = int.Parse(f.InnerText); break;
                    case "fixedRanks": def.fixedRanks = int.Parse(f.InnerText); break;
                    case "rankGap": def.rankGap = int.Parse(f.InnerText); break;
                    case "echelon": def.echelon = float.Parse(f.InnerText, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "curve": def.curve = float.Parse(f.InnerText, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "square": def.square = bool.Parse(f.InnerText); break;
                    case "iconSample":
                        def.iconSample = new List<int>();
                        foreach (XmlNode li in f.ChildNodes)
                        {
                            if (li.Name == "li")
                                def.iconSample.Add(int.Parse(li.InnerText));
                        }
                        break;
                    case "rankOrder":
                        foreach (XmlNode li in f.ChildNodes)
                        {
                            if (li.Name == "li")
                                def.rankOrder.Add((FormationRole)Enum.Parse(typeof(FormationRole), li.InnerText));
                        }
                        break;
                }
            }
            return def;
        }
    }

    // RimWorld/Bannerlord look: off-white glyphs with a dark outline on transparent background.
    // Role by shape: melee (incl. pikes) = square, ranged = circle.
    static class Icons
    {
        const int Size = 128;
        static readonly Color Light = Color.FromArgb(255, 232, 228, 216);
        static readonly Color Dark = Color.FromArgb(235, 22, 22, 22);
        static readonly Color Shadow = Color.FromArgb(90, 0, 0, 0);

        public static void Draw(FormationDef def, string file)
        {
            // Sample army in rank order.
            List<FormationRole> roles = new List<FormationRole>();
            int each = def.fixedRanks == 1 ? 2 : (def.shape == FormationShape.Checker ? 8 : 6);
            int[] counts = { each, each, each }; // pike, melee, ranged
            if (def.iconSample != null && def.iconSample.Count == 3)
                counts = def.iconSample.ToArray();
            List<FormationRole> order = new List<FormationRole>(def.rankOrder);
            foreach (FormationRole r in Enum.GetValues(typeof(FormationRole)))
                if (!order.Contains(r)) order.Add(r);
            foreach (FormationRole r in order)
                for (int i = 0; i < counts[(int)r]; i++)
                    roles.Add(r);

            List<Verse.IntVec3> cells = FormationLayout.Build(def, roles, def.defaultWidth, def.defaultSpacing);

            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (Verse.IntVec3 c in cells)
            {
                minX = Math.Min(minX, c.x); maxX = Math.Max(maxX, c.x);
                minZ = Math.Min(minZ, c.z); maxZ = Math.Max(maxZ, c.z);
            }
            RectangleF area = new RectangleF(10, 34, Size - 20, Size - 42);
            float spanX = maxX - minX + 1, spanZ = maxZ - minZ + 1;
            float cell = Math.Min(17f, Math.Min(area.Width / spanX, area.Height / spanZ));
            float ox = area.X + (area.Width - spanX * cell) / 2f;
            float oy = area.Y + (area.Height - spanZ * cell) / 2f;
            // Loose formations: bigger tokens so the gaps read as gaps, not as tiny dots.
            float tokenSize = Math.Min(14f, cell * 0.66f * (def.defaultSpacing > 1 ? def.defaultSpacing * 0.6f : 1f));

            using (Bitmap bmp = new Bitmap(Size, Size))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                // shadow, outline, fill
                for (int pass = 0; pass < 3; pass++)
                {
                    float off = pass == 0 ? 2.5f : 0f;
                    Color col = pass == 0 ? Shadow : (pass == 1 ? Dark : Light);
                    float grow = pass == 2 ? 0f : 2.2f;
                    Arrow(g, off, col, grow);
                    for (int i = 0; i < cells.Count; i++)
                    {
                        float cx = ox + (cells[i].x - minX + 0.5f) * cell + off;
                        float cy = oy + (maxZ - cells[i].z + 0.5f) * cell + off;
                        Token(g, roles[i], cx, cy, tokenSize, col, grow, pass == 2);
                    }
                }
                bmp.Save(file, ImageFormat.Png);
            }
        }

        static void Arrow(Graphics g, float off, Color col, float grow)
        {
            PointF[] pts =
            {
                new PointF(64, 6 - grow), new PointF(80 + grow, 22 + grow * 0.5f), new PointF(70 + grow, 22 + grow * 0.5f),
                new PointF(64, 16 + grow), new PointF(58 - grow, 22 + grow * 0.5f), new PointF(48 - grow, 22 + grow * 0.5f)
            };
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new PointF(pts[i].X + off, pts[i].Y + off);
            using (SolidBrush b = new SolidBrush(col))
                g.FillPolygon(b, pts);
        }

        static void Token(Graphics g, FormationRole role, float cx, float cy, float s, Color col, float grow, bool fillPass)
        {
            float h = s / 2f + grow;
            using (SolidBrush b = new SolidBrush(col))
            {
                if (role == FormationRole.Ranged)
                {
                    float r = s * 0.55f + grow;
                    g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
                    return;
                }
                g.FillRectangle(b, cx - h, cy - h, 2 * h, 2 * h);
            }
        }

        public static void Sheet(List<string> files, string path)
        {
            int cols = 7, cellPx = 140;
            int rows = (files.Count + cols - 1) / cols;
            using (Bitmap sheet = new Bitmap(cols * cellPx, rows * cellPx))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(255, 42, 43, 44));
                for (int i = 0; i < files.Count; i++)
                {
                    int x = (i % cols) * cellPx, y = (i / cols) * cellPx;
                    using (Brush bg = new SolidBrush(Color.FromArgb(255, 60, 62, 64)))
                        g.FillRectangle(bg, x + 4, y + 4, cellPx - 8, cellPx - 8);
                    using (Image img = Image.FromFile(files[i]))
                        g.DrawImage(img, x + 6, y + 6, 128, 128);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }
}
