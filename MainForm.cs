namespace Jarvis;

/// <summary>Главное окно: тёмный интерфейс в стиле JARVIS — градиентная шапка, круглая кнопка микрофона,
/// журнал в виде чата и подсказки команд.</summary>
public sealed class MainForm : Form
{
    // ----- Шапка -----
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 96 };

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

    // ----- Режим пробуждения по слову «Джарвис» -----
    private static readonly string[] WakeWords =
        { "джарвис", "джервис", "джарвес", "джервис", "джарвас", "джервас", "jarvis", "jarvas" };
    private const int AwakeSeconds = 10; // сколько секунд слушает команду после пробуждения
    private DateTime _awakeUntil = DateTime.MinValue;
    private readonly System.Windows.Forms.Timer _awakeTimer = new() { Interval = AwakeSeconds * 1000 };

    // ----- Таймеры -----
    private readonly TimerManager _timers = new();
    private readonly Label _timersLabel = new()
    {
        AutoSize = false,
        Width = 186,
        Height = 74,
        Text = "Таймеры: нет",
        ForeColor = Theme.TextDim,
        Font = new Font("Segoe UI", 8.5f),
        Location = new Point(14, 268)
    };

    public MainForm()
    {
        Text = "JARVIS — голосовой ассистент";
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 500);
        Size = new Size(960, 640);

        _processor = new CommandProcessor(msg => AppendLine("  " + msg, Theme.TextDim), _timers);

        BuildLayout();

        _micButton.Clicked += async (_, _) => await ToggleAsync();
        _btnClear.Click += (_, _) => _log.Clear();
        _chkSpeak.CheckedChanged += (_, _) => _speaker.Enabled = _chkSpeak.Checked;
        _timers.Changed += () => { try { BeginInvoke(UpdateTimersLabel); } catch { /* окно закрыто */ } };
        _timers.Fired += label =>
        {
            var msg = "Время вышло: " + label;
            try
            {
                BeginInvoke(() =>
                {
                    System.Media.SystemSounds.Exclamation.Play();
                    AppendLine("⏰ " + msg, Theme.Warning);
                    _speaker.Speak(msg);
                    UpdateTimersLabel();
                });
            }
            catch { /* окно закрыто */ }
        };
        _awakeTimer.Tick += (_, _) =>
        {
            _awakeTimer.Stop();
            if (_engine != null)
            {
                _engine.Awake = false; // усыпаем обратно
                SetStatus("СЛУШАЮ", Theme.Accent);
            }
        };
    }

    /// <summary>Подгоняет размер окна под рабочую область экрана (с учётом масштаба DPI).</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        float scale = DeviceDpi / 96f;
        int w = (int)Math.Min(960, wa.Width * 0.92f / scale);
        int h = (int)Math.Min(640, wa.Height * 0.88f / scale);
        if (w >= MinimumSize.Width && h >= MinimumSize.Height)
            Size = new Size(w, h);

        _ = LoadModelAsync();
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
            using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
            using var titleBrush = new SolidBrush(Theme.Accent);
            g.DrawString("JARVIS", titleFont, titleBrush, new Point(20, 10));

            using var subFont = new Font("Segoe UI", 9.5f);
            using var subBrush = new SolidBrush(Theme.TextDim);
            g.DrawString("голосовой ассистент для домашнего компьютера", subFont, subBrush,
                new Point(22, 58));

            // Статус справа
            var pillSize = _statusPill.Size;
            _statusPill.Location = new Point(_header.Width - pillSize.Width - 24, 34);
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
            Text = "F7 — вкл/выкл",
            AutoSize = false,
            Width = 186,
            Height = 20,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(14, 244)
        };
        _sidebar.Controls.Add(hintLabel);
        _sidebar.Controls.Add(_timersLabel);

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
            "открой документы", "закрой браузер", "заблокируй компьютер", "выключи компьютер",
            "таймер на 10 минут", "напомни через час позвонить"
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

        AppendLine("Джарвис запущен. Микрофон включится автоматически.", Theme.TextDim);
        AppendLine("Обращайтесь по имени: «Джарвис, открой блокнот», «Джарвис, который час».", Theme.TextDim);
        AppendLine("Скажите «Джарвис» без команды — он ответит и будет слушать 10 секунд.", Theme.TextDim);
        AppendLine("", Theme.TextDim);
    }

    /// <summary>F7 — старт/стоп микрофона.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F7)
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
            if (_modelPath.Contains("0.42"))
                AppendLine("Загружена большая модель распознавания — распознаёт с первого раза.", Theme.Success);
            AppendLine("Модель загружена. Включаю микрофон...", Theme.TextDim);
            _ = ToggleAsync(); // слушаем всегда — ждём слова «Джарвис»
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
                    e.WakeDetected += () =>
                        BeginInvoke(OnWakeDetected);
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
            StopEngine();
        }
    }

    private void StopEngine()
    {
        _engine?.Stop();
        _engine?.Dispose();
        _engine = null;
        _awakeTimer.Stop();
        _awakeUntil = DateTime.MinValue;
        _micButton.IsListening = false;
        _partialLabel.Text = "";
        SetStatus("ОСТАНОВЛЕН", Theme.TextDim);
    }

    /// <summary>Сработало слово пробуждения (из грамматического распознавателя).</summary>
    private void OnWakeDetected()
    {
        if (_engine == null) return;
        _awakeUntil = DateTime.Now.AddSeconds(AwakeSeconds);
        _awakeTimer.Stop();
        _awakeTimer.Start();
        SetStatus("СЛУШАЮ ВАС", Theme.AccentPurple);
        AppendLine("  🔵 Проснулся. Говорите команду (10 секунд).", Theme.Accent);
        _speaker.Speak("Слушаю.");
    }

    /// <summary>Обработка сказанной фразы: пробуждение по слову «Джарвис», затем команда.</summary>
    private void OnFinalPhrase(string text)
    {
        _partialLabel.Text = "";
        var t = text.Trim().ToLowerInvariant().Replace('ё', 'е');
        DebugLog.Write($"ФРАЗА: [{text}]");
        if (t.Length == 0) return;

        AppendLine("  Вы: " + text, Theme.UserLine);

        bool hasWake = WakeWords.Any(w => t.Contains(w));

        // Убираем обращение из фразы — остаётся только команда
        string command = t;
        foreach (var w in WakeWords)
            command = command.Replace(w, " ");
        command = string.Join(' ', command.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        DebugLog.Write($"wake={hasWake}, команда=[{command}]");

        if (hasWake)
        {
            // Проснулись: 10 секунд на команду
            if (_engine != null) _engine.Awake = true;
            _awakeUntil = DateTime.Now.AddSeconds(AwakeSeconds);
            _awakeTimer.Stop();
            _awakeTimer.Start();
            SetStatus("СЛУШАЮ ВАС", Theme.AccentPurple);

            if (command.Length == 0)
            {
                AppendLine("  Джарвис: Да, я слушаю вас.", Theme.Text);
                _speaker.Speak("Слушаю вас.");
                return;
            }

            // «Джарвис, выключись» — усыпляем ассистента
            var words = command.Split(' ');
            if (words.Contains("выключись") || words.Contains("стоп") || words.Contains("спи") ||
                words.Contains("отбой") || words.Contains("остановись"))
            {
                AppendLine("  Джарвис: Усыпляюсь. Скажите «Джарвис», чтобы разбудить.", Theme.Text);
                _speaker.Speak("Усыпляюсь.");
                StopEngine();
                return;
            }
        }
        else if (DateTime.Now > _awakeUntil)
        {
            // Не проснулся и не в активном окне — игнорируем фоновую речь
            AppendLine("  (нет обращения «Джарвис» — пропущено)", Theme.TextDim);
            return;
        }

        ExecuteCommand(command);
    }

    private void ExecuteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        string? response;
        try
        {
            response = _processor.Process(command);
        }
        catch (Exception ex)
        {
            AppendLine("  ⚠ Ошибка выполнения команды: " + ex.Message, Theme.Danger);
            return;
        }

        if (response != null)
        {
            AppendLine("  Джарвис: " + response, Theme.Text);
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

    /// <summary>Обновляет список активных таймеров на боковой панели.</summary>
    private void UpdateTimersLabel()
    {
        var active = _timers.ActiveText;
        _timersLabel.Text = active == "нет" ? "Таймеры: нет" : "Таймеры:\n" + active;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _engine?.Dispose();
        _speaker.Dispose();
        base.OnFormClosing(e);
    }
}
