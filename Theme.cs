namespace Jarvis;

/// <summary>Цветовая тема приложения в стиле JARVIS (тёмная, с неоновым акцентом).</summary>
public static class Theme
{
    public static readonly Color Background   = Color.FromArgb(11, 14, 20);    // #0B0E14
    public static readonly Color Panel        = Color.FromArgb(18, 22, 31);     // #12161F
    public static readonly Color PanelLight   = Color.FromArgb(26, 34, 51);     // #1A2233
    public static readonly Color Border       = Color.FromArgb(42, 53, 80);     // #2A3550
    public static readonly Color Accent       = Color.FromArgb(0, 229, 255);    // #00E5FF
    public static readonly Color AccentDark   = Color.FromArgb(0, 140, 190);    // #008CBE
    public static readonly Color AccentPurple = Color.FromArgb(124, 77, 255);   // #7C4DFF
    public static readonly Color Text         = Color.FromArgb(230, 237, 243);  // #E6EDF3
    public static readonly Color TextDim      = Color.FromArgb(139, 148, 158);  // #8B949E
    public static readonly Color Success      = Color.FromArgb(46, 165, 95);    // #2EA55F
    public static readonly Color Warning      = Color.FromArgb(210, 153, 34);   // #D29922
    public static readonly Color Danger       = Color.FromArgb(248, 81, 73);    // #F85149
    public static readonly Color UserLine     = Color.FromArgb(0, 200, 230);    // реплика пользователя

    /// <summary>Рисует скруглённый прямоугольник.</summary>
    public static void FillRounded(Graphics g, Rectangle rect, int radius, Color color)
    {
        using var path = RoundedRect(rect, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>Рисует обводку скруглённого прямоугольника.</summary>
    public static void DrawRounded(Graphics g, Rectangle rect, int radius, Color color, float width = 1f)
    {
        using var path = RoundedRect(rect, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        if (d < 2)
        {
            path.AddRectangle(rect);
            return path;
        }
        var arc = new Rectangle(rect.X, rect.Y, d, d);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - d;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - d;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
