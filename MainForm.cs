namespace Jarvis;

/// <summary>Главное окно: тёмный интерфейс в стиле JARVIS — градиентная шапка, круглая кнопка микрофона,
/// журнал в виде чата и подсказки команд.</summary>
public sealed class MainForm : Form
{
    // ----- Шапка -----
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 88 };

    // ----- Левая панель -----
    private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Width = 214 };
    private readonly MicButton _micButton = new() { Location = new Point(49, 28) };
    private readonly StatusPill _statusPill = new() { Location = new Point(36, 160) };
    private readonly CheckBox _chkSpeak = new()
    {
        Text = "Отвечать голосом",
        Checked = true,
        AutoSize = true,
        ForeColor = Theme.Text,
        Location = new Point(50, 210),
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnClear = new()
    {
        Text = "Очистить журнал",
        Width = 154,
        Height = 34,
        Location = new Point(30, 470),
        FlatStyle = FlatStyle.Flat,
        ForeColor = Theme.TextDim,
        BackColor = Theme.PanelLight,
        Cursor = Cursors.Hand
    };

    // ----- Центр -----
    private readonly RichTextBox _log = new()
    {
        BackColor = Theme.Background,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.None,
        ReadOnly = true,
        Font = new Font("Segoe UI", 10.5f),
        DetectUrls = true,
        Dock = DockStyle.Fill,
        Padding = new Padding(8)
    };

    private readonly Label _partialLabel = new()
    {
        Dock = DockStyle.Fill,
        Text = "",
        Font = new Font("Segoe UI", 10, FontStyle.Italic),
        ForeColor = Theme.Accent,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(18, 0, 0, 0)
    };

    private readonly Speaker _speaker = new();
    private readonly CommandProcessor _processor;
    private VoiceEngine? _engine;
    private string? _modelPath;
    private bool _busy;

    public MainForm()
    {
        Text = "JARVIS — голосовой ассистент";
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(960, 660);

        _processor = new CommandProcessor(msg => AppendLine("  " + msg, Theme.TextDim));

        BuildLayout();

        _micButton.Clicked += async (_, _) => await ToggleAsync();
        _btnClear.Click += (_, _) => _log.Clear();
        _chkSpeak.CheckedChanged += (_, _) => _speaker.Enabled = _chkSpeak.Checked;
        Load += async (_, _) => await LoadModelAsync();
    }

    private void BuildLayout()
    {
        // Шапка: градиент + заголовок + подзаголовок + пилюля статуса справа
        _header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                _header.ClientRectangle, Color.FromArgb(13, 18, 32), Color.FromArgb(19, 26, 43),
                System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal);
            g.FillRectangle(gradient, _header.ClientRectangle);

            // Неоновая линия внизу шапки
            using var line = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Point(0, 0), new Point(_header.Width, 0), Theme.Accent, Theme.AccentPurple);
            g.FillRectangle(line, 0, _header.Height - 2, _header.Width, 2);

            // Заголовок
            using var titleFont = new Font("Segoe UI", 24, FontStyle.Bold);
            using var titleBrush = new SolidBrush(Theme.Accent);
            g.DrawString("JARVIS", titleFont, titleBrush, new Point(20, 8));

            using var subFont = new Font("Segoe UI", 9.5f);
            using var subBrush = new SolidBrush(Theme.TextDim);
            g.DrawString("голосовой ассистент для домашнего компьютера", subFont, subBrush,
                new Point(22, 0), new StringFormat { LineAlignment = StringAlignment.Center });

            // Статус справа
            var pillSize = _statusPill.Size;
            _statusPill.Location = new Point(_header.Width - pillSize.Width - 22, 28);
            _statusPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        };
        _header.Resize += (_, _) => _header.Invalidate();

        // Левая панель
        _sidebar.BackColor = Theme.Panel;
        _sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
        };
        _sidebar.Controls.AddRange(new Control[] { _micButton, _statusPill, _chkSpeak, _btnClear });

        var hintLabel = new Label
        {
            Text = "F9 — вкл/выкл микрофон",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(44, 244)
        };
        _sidebar.Controls.Add(hintLabel);

        // Стилизация кнопок
        _btnClear.FlatAppearance.BorderColor = Theme.Border;
        _btnClear.FlatAppearance.MouseOverBackColor = Theme.AccentPurple;
        _btnClear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _chkSpeak.FlatStyle = FlatStyle.Flat;
        _chkSpeak.FlatAppearance.CheckedBackColor = Theme.AccentDark;

        // Чат
        var chatTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "  ЖУРНАЛ КОМАНД",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var chips = new ChipsPanel(new[]
        {
            "открой блокнот", "открой youtube", "новая вкладка", "закрой вкладку",
            "обнови страницу", "громкость 50", "который час", "поищи погоду в Москве",
            "открой документы", "закрой браузер", "заблокируй компьютер", "выключи компьютер"
        });

        var centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(12, 0, 12, 0) };
        centerPanel.Controls.Add(_log);
        centerPanel.Controls.Add(_partialLabel);
        centerPanel.Controls.Add(chatTitle);

        // Порядок добавления определяет докирование
        Controls.Add(centerPanel);
        Controls.Add(_sidebar);
        Controls.Add(chips);
        Controls.Add(_header);
        _header.Controls.Add(_statusPill);

        AppendLine("Джарвис запущен. Нажмите кнопку микрофона или F9 и говорите.", Theme.TextDim);
        AppendLine("Пример: «открой блокнот», «громче», «который час», «поищи погоду в Москве».",
            Theme.TextDim);
        AppendLine("", Theme.TextDim);
    }

    /// <summary>F9 — старт/стоп микрофона.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F9)
        {
            _ = ToggleAsync();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task LoadModelAsync()
    {
        _micButton.Enabled = false;
        SetStatus("ЗАГРУЗКА МОДЕЛИ...", Theme.Warning);
        try
        {
            _modelPath = await Task.Run(() => ModelManager.EnsureModel(msg => BeginInvoke(() => SetStatus("ЗАГРУЗКА МОДЕЛИ...", Theme.Warning))));
            SetStatus("ГОТОВ", Theme.Success);
            AppendLine("Модель распознавания речи загружена. Микрофон готов к работе.", Theme.TextDim);
        }
        catch (Exception ex)
        {
            SetStatus("ОШИБКА", Theme.Danger);
            MessageBox.Show(
                "Не удалось загрузить модель распознавания речи:\n" + ex.Message +
                "\n\nПроверьте подключение к интернету и перезапустите приложение.",
                "JARVIS", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _micButton.Enabled = true;
        }
    }

    private async Task ToggleAsync()
    {
        if (_busy) return;

        if (_engine == null)
        {
            if (_modelPath == null) return;
            _busy = true;
            try
            {
                SetStatus("ЗАПУСК...", Theme.Warning);
                var engine = await Task.Run(() =>
                {
                    var e = new VoiceEngine(_modelPath);
                    e.PartialResult += text =>
                        BeginInvoke(() => _partialLabel.Text = "  Слышу: " + text);
                    e.FinalResult += text =>
                        BeginInvoke(() => OnFinalPhrase(text));
                    e.Start();
                    return e;
                });
                _engine = engine;
                _micButton.IsListening = true;
                SetStatus("СЛУШАЮ", Theme.Accent);
                AppendLine("———————————————", Theme.Border);
            }
            catch (Exception ex)
            {
                SetStatus("НЕТ МИКРОФОНА", Theme.Danger);
                MessageBox.Show("Ошибка доступа к микрофону:\n" + ex.Message, "JARVIS",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
            }
        }
        else
        {
            _engine.Stop();
            _engine.Dispose();
            _engine = null;
            _micButton.IsListening = false;
            _partialLabel.Text = "";
            SetStatus("ОСТАНОВЛЕН", Theme.TextDim);
        }
    }

    private void OnFinalPhrase(string text)
    {
        _partialLabel.Text = "";
        if (string.IsNullOrWhiteSpace(text)) return;

        AppendLine("  Вы: " + text, Theme.UserLine);

        string? response;
        try
        {
            response = _processor.Process(text);
        }
        catch (Exception ex)
        {
            AppendLine("  ⚠ Ошибка выполнения команды: " + ex.Message, Theme.Danger);
            return;
        }

        if (response != null)
        {
            AppendLine("  " + response, Theme.Text);
            _speaker.Speak(response);
        }
        else
        {
            AppendLine("  Команда не распознана. Попробуйте сказать иначе.", Theme.TextDim);
        }
    }

    private void AppendLine(string text, Color color)
    {
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = color;
        _log.AppendText(text + Environment.NewLine);
        _log.SelectionColor = _log.ForeColor;
        _log.ScrollToCaret();
    }

    private void SetStatus(string text, Color dotColor)
    {
        _statusPill.SetStatus(text, dotColor);
        _header.Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _engine?.Dispose();
        _speaker.Dispose();
        base.OnFormClosing(e);
    }
}
