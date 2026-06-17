using System.Drawing.Drawing2D;

namespace DiskGrowthMonitor.App;

internal static class AppIconFactory
{
    public static Icon CreateIcon(int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var bg = new LinearGradientBrush(
            new Rectangle(0, 0, size, size),
            Color.FromArgb(16, 99, 116),
            Color.FromArgb(31, 172, 154),
            LinearGradientMode.ForwardDiagonal);
        graphics.FillEllipse(bg, 1, 1, size - 3, size - 3);

        using var diskBrush = new SolidBrush(Color.FromArgb(246, 252, 250));
        var disk = new RectangleF(size * 0.2f, size * 0.24f, size * 0.6f, size * 0.5f);
        graphics.FillRoundedRectangle(diskBrush, disk, size * 0.08f);

        using var slot = new SolidBrush(Color.FromArgb(38, 70, 76));
        graphics.FillRectangle(slot, size * 0.32f, size * 0.34f, size * 0.36f, size * 0.08f);

        using var pulsePen = new Pen(Color.FromArgb(255, 139, 70), Math.Max(2, size / 12))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var points = new[]
        {
            new PointF(size * 0.23f, size * 0.62f),
            new PointF(size * 0.38f, size * 0.62f),
            new PointF(size * 0.47f, size * 0.49f),
            new PointF(size * 0.58f, size * 0.71f),
            new PointF(size * 0.74f, size * 0.48f)
        };
        graphics.DrawLines(pulsePen, points);

        using var textBrush = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", size * 0.26f, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString("C", font, textBrush, size * 0.39f, size * 0.02f);

        return Icon.FromHandle(bitmap.GetHicon());
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
