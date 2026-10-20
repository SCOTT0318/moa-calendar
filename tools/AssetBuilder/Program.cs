using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var frames = new List<(int Size, byte[] Png)>();
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 256d, size / 256d));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(59, 105, 220)), null, new Rect(8, 8, 240, 240), 54, 54);
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(46, 54, 164, 152), 16, 16);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(213, 227, 255)), null, new Rect(46, 76, 164, 34));
                var pen = new Pen(Brushes.White, 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(82, 40), new Point(82, 74)); dc.DrawLine(pen, new Point(174, 40), new Point(174, 74));
                foreach (var p in new[] { new Point(82, 134), new Point(124, 134), new Point(166, 134), new Point(82, 171) }) dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(59, 105, 220)), null, p, 9, 9);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(30, 155, 105)), new Pen(Brushes.White, 7), new Rect(121, 152, 104, 74), 14, 14);
                dc.DrawLine(new Pen(Brushes.White, 6), new Point(135, 170), new Point(173, 195)); dc.DrawLine(new Pen(Brushes.White, 6), new Point(173, 195), new Point(211, 170));
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); var data = stream.ToArray(); frames.Add((size, data));
            if (size == 256) File.WriteAllBytes(Path.Combine(output, "moa.png"), data);
        }
        using var file = File.Create(Path.Combine(output, "moa.ico")); using var writer = new BinaryWriter(file);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
        var offset = 6 + frames.Count * 16;
        foreach (var (size, png) in frames) { writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(png.Length); writer.Write(offset); offset += png.Length; }
        foreach (var frame in frames) writer.Write(frame.Png);
    }
}
