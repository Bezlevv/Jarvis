namespace Jarvis;

/// <summary>Круглая кнопка микрофона с пульсирующим ореолом во время прослушивания.</summary>
public sealed class MicButton : Control
{
    private readonly System.Windows.Forms.Timer _pulseTimer = new() { Interval = 40 };
    private float _phase;
    private bool _active;

    public event EventHandler? Clicked;

    public MicButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(116, 116);
        Cursor = Cursors.Hand;
        _pulseTimer.Tick += (_, _) =>
        {
            _phase += 0.16f;
            if (_phase > MathF.PI * 2) _phase -= MathF.PI * 2;
            Invalidate();
        };
    }

    /// <summary>Микрофон активен (слушает).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsListening
    {
        get => _active;
        set
        {
            _active = value;
            if (value) _pulseTimer.Start(); else { _pulseTimer.Stop(); _phase = 0; Invalidate(); }
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var center = new PointF(Width / 2f, Height / 2f);
        float radius = Math.Min(Width, Height) / 2f - 6;

        if (_active)
        {
            // Пульсирующие кольца-«волны» вокруг микрофона
            for (int i = 0; i < 2; i++)
            {
                float wave = (_phase + i * MathF.PI) % (MathF.PI * 2);
                float t = wave / (MathF.PI * 2);
                float ringR = radius * (1.0f + 0.28f * t);
                int alpha = (int)(70 * (1 - t));
                if (alpha <= 0) continue;
                using var pen = new Pen(Color.FromArgb(alpha, Theme.Accent), 2.2f);
                g.DrawEllipse(pen, center.X - ringR, center.Y - ringR, ringR * 2, ringR * 2);
            }
        }

        // Основной круг
        var circleRect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        Color inner = _active ? Theme.AccentDark : Theme.PanelLight;
        Color outer = _active ? Theme.Accent : Theme.Border;
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(circleRect, inner, outer,
                   System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal))
            g.FillEllipse(brush, circleRect);
        using (var pen = new Pen(_active ? Theme.Accent : Theme.Border, _active ? 2.2f : 1.4f))
            g.DrawEllipse(pen, circleRect);

        // Иконка микрофона
        Color glyph = _active ? Color.FromArgb(8, 20, 26) : Theme.Accent;
        using (var pen = new Pen(glyph, 3.4f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        })
        {
            // Капсула (головка микрофона)
            var capsule = new RectangleF(center.X - 11, center.Y - 26, 22, 32);
            g.DrawArc(pen, capsule, 180, 180);
            g.DrawLine(pen, center.X - 11, center.Y - 10, center.X - 11, center.Y + 4);
            g.DrawLine(pen, center.X + 11, center.Y - 10, center.X + 11, center.Y + 4);
            g.DrawArc(pen, new RectangleF(center.X - 11, center.Y - 12, 22, 20), 0, 180);
            // Дужка
            g.DrawArc(pen, new RectangleF(center.X - 18, center.Y - 12, 36, 32), 0, 180);
            // Стойка и основание
            g.DrawLine(pen, center.X, center.Y + 20, center.X, center.Y + 28);
            g.DrawLine(pen, center.X - 12, center.Y + 28, center.X + 12, center.Y + 28);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pulseTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Пилюля статуса: цветной индикатор + подпись (например, «СЛУШАЮ»).</summary>
public sealed class StatusPill : Control
{
    private string _text = "ОСТАНОВЛЕН";
    private Color _dotColor = Theme.TextDim;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(140, 30);
    }

    public void SetStatus(string text, Color dotColor)
    {
        _text = text;
        _dotColor = dotColor;
        using var g = CreateGraphics();
        var size = TextRenderer.MeasureText(_text, Font);
        Width = size.Width + 42;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), Height / 2, Theme.PanelLight);
        Theme.DrawRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), Height / 2, Theme.Border);

        using (var brush = new SolidBrush(_dotColor))
            g.FillEllipse(brush, 12, Height / 2 - 4, 8, 8);

        using (var brush = new SolidBrush(Theme.TextDim))
            g.DrawString(_text, Font, brush,
                new RectangleF(28, 0, Width - 32, Height),
                new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });
    }
}

/// <summary>Панель с подсказками-«чипами» примеров команд.</summary>
public sealed class ChipsPanel : Control
{
    private readonly string[] _chips;
    private readonly List<Rectangle> _rects = new();

    public ChipsPanel(string[] chips)
    {
        _chips = chips;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Bottom;
        Height = 74;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        _rects.Clear();

        int x = 14, y = 26;
        foreach (var chip in _chips)
        {
            var size = TextRenderer.MeasureText(chip, Font);
            var rect = new Rectangle(x, y, size.Width + 26, 28);
            if (rect.Right > Width - 14) break;

            Theme.FillRounded(g, rect, 14, Theme.Panel);
            Theme.DrawRounded(g, rect, 14, Theme.Border);
            using (var brush = new SolidBrush(Theme.TextDim))
                g.DrawString(chip, Font, brush, new Point(rect.X + 13, rect.Y + 6));

            _rects.Add(rect);
            x = rect.Right + 10;
        }
    }
}
