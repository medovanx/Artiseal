using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the Artiseal icon and writes app.ico plus PNG previews.
// Usage: IconMaker <output folder> [monogram|sealed-file|commit-seal] | IconMaker <output folder> --sheet
string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

Dictionary<string, Action<DrawingContext, bool>> concepts = new()
{
    ["monogram"] = Concepts.Monogram,
    ["sealed-file"] = Concepts.SealedFile,
    ["commit-seal"] = Concepts.CommitSeal,
};

if (args.Contains("--sheet"))
{
    File.WriteAllBytes(Path.Combine(outDir, "concepts.png"), Sheet.Render(concepts));
    Console.WriteLine("wrote concepts.png");

    return;
}

string pick = args.Length > 1 ? args[1] : "monogram";
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
List<byte[]> pngs = [.. sizes.Select(size => Png.Encode(Png.Render(concepts[pick], size)))];

foreach ((int size, byte[] png) in sizes.Zip(pngs))
{
    if (size is 256 or 48 or 16)
    {
        File.WriteAllBytes(Path.Combine(outDir, $"icon-{size}.png"), png);
    }
}
File.WriteAllBytes(Path.Combine(outDir, "app.ico"), Ico.Build(sizes, pngs));
Console.WriteLine($"wrote app.ico ({pick}, {sizes.Length} sizes)");

static class Concepts
{
    // Everything is drawn on a 256 grid; "tiny" (24 px and under) drops detail that would blur.
    private static readonly Color WaxLight = Color.FromRgb(0x2E, 0x9A, 0x74);
    private static readonly Color WaxDark = Color.FromRgb(0x13, 0x5A, 0x42);
    private static readonly Color WaxEdge = Color.FromRgb(0x0E, 0x45, 0x33);

    public static void Monogram(DrawingContext dc, bool tiny)
    {
        Point c = new(128, 130);
        Wax(dc, c, 112, tiny);
        Press(dc, c, 82, tiny);

        // An "A" whose crossbar is the tick: the mark says "Artiseal" and "verified" at once.
        Pen stroke = Stroke(tiny ? 30 : 20);
        StreamGeometry a = Path(g =>
        {
            g.BeginFigure(new Point(84, 186), false, false);
            g.LineTo(new Point(128, 74), true, true);
            g.LineTo(new Point(172, 186), true, true);
        });
        dc.DrawGeometry(null, stroke, a);

        if (tiny)
        {
            // Too small for the tick to read: a plain crossbar keeps it an "A" rather than a "^".
            dc.DrawLine(Stroke(24), new Point(104, 148), new Point(152, 148));
        }
        else
        {
            StreamGeometry tick = Path(g =>
            {
                g.BeginFigure(new Point(96, 146), false, false);
                g.LineTo(new Point(116, 164), true, true);
                g.LineTo(new Point(176, 112), true, true);
            });
            dc.DrawGeometry(null, Stroke(16), tick);
        }
    }

    public static void SealedFile(DrawingContext dc, bool tiny)
    {
        // A page with a folded corner, sealed at the bottom right: "this file is sealed".
        StreamGeometry page = Path(g =>
        {
            g.BeginFigure(new Point(46, 18), true, true);
            g.LineTo(new Point(158, 18), true, true);
            g.LineTo(new Point(206, 66), true, true);
            g.LineTo(new Point(206, 222), true, true);
            g.LineTo(new Point(46, 222), true, true);
        });
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xF7, 0xF5, 0xF0)), new Pen(new SolidColorBrush(Color.FromRgb(0x5C, 0x57, 0x4F)), tiny ? 12 : 7), page);

        StreamGeometry fold = Path(g =>
        {
            g.BeginFigure(new Point(158, 18), true, true);
            g.LineTo(new Point(158, 66), true, true);
            g.LineTo(new Point(206, 66), true, true);
        });
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xDD, 0xD8, 0xCE)), new Pen(new SolidColorBrush(Color.FromRgb(0x5C, 0x57, 0x4F)), tiny ? 12 : 7), fold);

        if (!tiny)
        {
            Pen line = new(new SolidColorBrush(Color.FromRgb(0xB8, 0xB2, 0xA6)), 10) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(line, new Point(76, 70), new Point(130, 70));
            dc.DrawLine(line, new Point(76, 100), new Point(176, 100));
            dc.DrawLine(line, new Point(76, 130), new Point(150, 130));
        }

        Point c = new(166, 182);
        Wax(dc, c, tiny ? 70 : 64, tiny);
        Press(dc, c, tiny ? 44 : 42, tiny);
        dc.DrawGeometry(null, Stroke(tiny ? 18 : 13), Path(g =>
        {
            g.BeginFigure(new Point(146, 183), false, false);
            g.LineTo(new Point(160, 197), true, true);
            g.LineTo(new Point(188, 168), true, true);
        }));
    }

    public static void CommitSeal(DrawingContext dc, bool tiny)
    {
        Point c = new(128, 130);
        Wax(dc, c, 112, tiny);
        Press(dc, c, 82, tiny);

        // A commit line pressed into the wax: built from these commits.
        Pen rail = Stroke(tiny ? 22 : 14);
        dc.DrawLine(rail, new Point(128, 70), new Point(128, 190));

        SolidColorBrush hole = new(Color.FromRgb(0x18, 0x6A, 0x4E));
        double r = tiny ? 22 : 17;
        foreach (double y in tiny ? new[] { 92.0, 168.0 } : new[] { 86.0, 130.0, 174.0 })
        {
            dc.DrawEllipse(hole, Stroke(tiny ? 14 : 10), new Point(128, y), r, r);
        }
    }

    private static void Wax(DrawingContext dc, Point c, double outer, bool tiny)
    {
        double wobble = tiny ? 0.5 : 1;
        StreamGeometry blob = Path(g =>
        {
            const int Steps = 720;

            for (int i = 0; i <= Steps; i++)
            {
                double a = 2 * Math.PI * i / Steps;
                double r = outer * (1 + wobble * (0.035 * Math.Sin(3 * a + 0.4) + 0.025 * Math.Sin(5 * a + 2.1) + 0.018 * Math.Sin(9 * a + 1.3)));
                Point p = new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));

                if (i == 0)
                {
                    g.BeginFigure(p, true, true);
                }
                else
                {
                    g.LineTo(p, true, true);
                }
            }
        });
        RadialGradientBrush wax = new(WaxLight, WaxDark) { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.42), RadiusX = 0.75, RadiusY = 0.75 };
        dc.DrawGeometry(wax, new Pen(new SolidColorBrush(WaxEdge), tiny ? 6 : 3), blob);
    }

    private static void Press(DrawingContext dc, Point c, double r, bool tiny)
    {
        RadialGradientBrush sunk = new(Color.FromRgb(0x16, 0x63, 0x49), Color.FromRgb(0x1E, 0x7A, 0x5A)) { GradientOrigin = new Point(0.4, 0.3) };
        dc.DrawEllipse(sunk, null, c, r, r);

        if (!tiny)
        {
            LinearGradientBrush rim = new(Color.FromArgb(0x60, 0, 0, 0), Color.FromArgb(0x70, 0xB8, 0xF0, 0xD8), 90);
            dc.DrawEllipse(null, new Pen(rim, 6), c, r, r);
        }
    }

    private static Pen Stroke(double width) => new(Brushes.White, width)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    private static StreamGeometry Path(Action<StreamGeometryContext> draw)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext g = geometry.Open())
        {
            draw(g);
        }
        geometry.Freeze();

        return geometry;
    }
}

static class Png
{
    public static BitmapSource Render(Action<DrawingContext, bool> concept, int size)
    {
        DrawingVisual visual = new();

        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            concept(dc, size <= 24);
            dc.Pop();
        }
        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        return bitmap;
    }

    public static byte[] Encode(BitmapSource bitmap)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);

        return stream.ToArray();
    }
}

static class Sheet
{
    // One row per concept: 256, 48 and 16 px on a light and a dark strip, at true pixel size.
    public static byte[] Render(Dictionary<string, Action<DrawingContext, bool>> concepts)
    {
        const int RowH = 300, W = 720;
        DrawingVisual visual = new();

        using (DrawingContext dc = visual.RenderOpen())
        {
            int row = 0;

            foreach ((string name, Action<DrawingContext, bool> concept) in concepts)
            {
                double y = row * RowH;
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF6, 0xF5, 0xF2)), null, new Rect(0, y, W, RowH));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x22)), null, new Rect(480, y, 240, RowH));
                dc.DrawText(new FormattedText($"{row + 1}. {name}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 16, Brushes.Black, 1), new Point(16, y + 8));
                dc.DrawImage(Render(concept, 256), new Rect(16, y + 36, 256, 256));
                dc.DrawImage(Render(concept, 48), new Rect(300, y + 100, 48, 48));
                dc.DrawImage(Render(concept, 16), new Rect(380, y + 116, 16, 16));
                dc.DrawImage(Render(concept, 48), new Rect(520, y + 100, 48, 48));
                dc.DrawImage(Render(concept, 16), new Rect(620, y + 116, 16, 16));
                row++;
            }
        }
        RenderTargetBitmap bitmap = new(W, RowH * concepts.Count, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        return Png.Encode(bitmap);
    }

    private static BitmapSource Render(Action<DrawingContext, bool> concept, int size) => Png.Render(concept, size);
}

static class Ico
{
    // An .ico whose entries are PNGs (supported since Windows Vista).
    public static byte[] Build(int[] sizes, List<byte[]> pngs)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);

        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);

        int offset = 6 + 16 * sizes.Length;

        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (byte[] png in pngs)
        {
            w.Write(png);
        }

        return ms.ToArray();
    }
}
