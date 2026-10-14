using System.Globalization;
using System.Text.RegularExpressions;

namespace Jarvis;

/// <summary>
/// Разбирает распознанную фразу и выполняет соответствующую команду.
/// Сопоставление идёт по ключевым словам с учётом основ слов (подстрока),
/// поэтому «громче», «громкость», «громкости» распознаются одинаково.
/// </summary>
public sealed class CommandProcessor
{
    private static readonly string[] StopWords =
    {
        "в", "во", "на", "и", "а", "же", "бы", "мне", "мой", "моя", "моё", "мое", "please",
        "пожалуйста", "компьютер", "комп", "пк", "ассистент", "джарвис", "jarvis", "дай", "сделай"
    };

    private static readonly (string[] Keys, Action Launch)[] Apps =
    {
        (new[] { "блокнот", "notepad" }, () => SystemController.LaunchApp("notepad.exe")),
        (new[] { "калькулятор", "калькулятор", "calc" }, () => SystemController.LaunchApp("calc.exe")),
        (new[] { "проводник", "explorer" }, () => SystemController.LaunchApp("explorer.exe")),
        (new[] { "диспетчер задач", "диспетчер процессов" }, () => SystemController.LaunchApp("taskmgr.exe")),
        (new[] { "панель управлени" }, () => SystemController.LaunchApp("control.exe")),
        (new[] { "paint", "паинт" }, () => SystemController.LaunchApp("mspaint.exe")),
        (new[] { "блокировк", "экран блокиров" }, SystemController.Lock),
        (new[] { "браузер", "интернет", "веб-браузер", "гугл хром", "chrome", "яндекс браузер" },
            () => SystemController.OpenUrl("https://www.yandex.ru")),
        (new[] { "ютуб", "youtube" }, () => SystemController.OpenUrl("https://www.youtube.com")),
        (new[] { "вконтакте", "вк ", "vk" }, () => SystemController.OpenUrl("https://vk.com")),
        (new[] { "почт", "gmail", "гмейл" }, () => SystemController.OpenUrl("https://mail.google.com")),
        (new[] { "гугл", "google" }, () => SystemController.OpenUrl("https://www.google.com")),
        (new[] { "википеди", "wikipedia" }, () => SystemController.OpenUrl("https://ru.wikipedia.org")),
    };

    private readonly Action<string> _log;
    private readonly TimerManager _timers;
    private readonly List<CustomCommand> _custom;

    /// <summary>Ожидающее подтверждение действие (например, «закрыть браузер? да/нет»).</summary>
    private Action? _pending;

    public CommandProcessor(Action<string> log, TimerManager timers)
    {
        _log = log;
        _timers = timers;
        _custom = CommandLibrary.Load();
    }

    /// <summary>Выполняет команду. Возвращает текст ответа ассистента (или null, если команда не распознана).</summary>
    public string? Process(string rawText)
    {
        var t = Normalize(rawText);
        if (t.Length == 0) return null;

        // ---------- Подтверждение опасного действия ----------
        if (_pending != null)
        {
            var words = t.Split(' ');
            var action = _pending;
            _pending = null;
            if (words.Contains("да") || words.Contains("давай") || words.Contains("конечно") ||
                words.Contains("ок") || words.Contains("ага") || words.Contains("выполняй"))
            {
                action();
                return "Выполняю.";
            }
            if (words.Contains("нет") || words.Contains("отмена") || words.Contains("не") ||
                words.Contains("отбой"))
                return "Хорошо, отменил.";
            // произнесена другая команда — просто сбрасываем ожидание
        }

        // ---------- Пользовательские команды из commands.json ----------
        foreach (var cmd in _custom)
        {
            bool hit = false;
            foreach (var phrase in cmd.Phrases)
            {
                var p = Normalize(phrase);
                if (p.Length > 0 && t.Contains(p, StringComparison.Ordinal)) { hit = true; break; }
            }
            if (!hit) continue;

            switch (cmd.Action.Trim().ToLowerInvariant())
            {
                case "url":
                    SystemController.OpenUrl(cmd.Value);
                    return $"Открываю: {cmd.Name}.";
                case "search":
                    SystemController.OpenWebSearch(cmd.Value);
                    return $"Ищу в интернете: {cmd.Value}.";
                case "app":
                case "file":
                case "folder":
                    SystemController.LaunchApp(cmd.Value);
                    return $"Выполняю: {cmd.Name}.";
                case "reply":
                    return cmd.Value;
                default:
                    return $"Команда «{cmd.Name}» настроена неверно: неизвестный тип «{cmd.Action}».";
            }
        }

        // ---------- Приветствие и вежливость ----------
        if (Has(t, "привет", "здравствуй", "добрый день", "доброе утро", "добрый вечер", "хэй"))
            return "Привет! Я готов к работе.";
        if (Has(t, "спасибо", "благодарю"))
            return "Пожалуйста!";

        // ---------- Простая болтовня ----------
        if (Has(t, "тупой", "тупый", "дурак", "идиот", "глупый", "тупица"))
            return "Команды-то ваши неразборчивы. Попробуем ещё разок, я поднатужусь?";
        if (Has(t, "как дела", "как ты", "как жизнь", "как настроение"))
            return "Отлично: слушаю и жду ваших команд. У вас как настроение?";
        if (Has(t, "ты кто", "как тебя зовут", "представься", "ты человек", "ты живой"))
            return "Я Джарвис — голосовой ассистент вашего компьютера. Управляю системой и браузером.";
        if (Has(t, "молодец", "умница", "хороший ассистент", "спасибо что"))
            return "Спасибо, стараюсь.";
        if (Has(t, "шутка", "пошути", "анекдот"))
            return "Два программиста уходят в бар, потому что один минус один — это не два.";
        if (Has(t, "алиса", "alice"))
            return "Алиса — это голосовой ассистент Яндекса, а я Джарвис. Живу у вас на компьютере и ни с кем не путаюсь.";

        // ---------- Отмена выключения (до команд выключения!) ----------
        if (Has(t, "отмени выключ", "отмена выключ", "отмени перезагруз", "отмени завершение", "не выключай"))
        {
            SystemController.CancelShutdown();
            return "Хорошо, выключение отменено.";
        }

        // ---------- Время и дата ----------
        if (Has(t, "который час", "сколько времени", "текущее время", "которого часа"))
            return $"Сейчас {DateTime.Now:HH:mm}.";
        if (Has(t, "какое сегодня число", "какая сегодня дата", "какой сегодня день", "какое число"))
        {
            var culture = CultureInfo.GetCultureInfo("ru-RU");
            var day = culture.DateTimeFormat.GetDayName(DateTime.Now.DayOfWeek);
            return $"Сегодня {day}, {DateTime.Now.ToString("d MMMM yyyy", culture)}.";
        }

        // ---------- Погода (бесплатный API Open-Meteo) ----------
        if (Has(t, "погод", "прогноз", "какая температура", "градус за окном"))
        {
            var city = Strip(t, "погода", "погоды", "погодке", "прогноз", "прогноз погоды",
                "какая", "какой", "сколько", "будет", "завтра", "сегодня",
                "послезавтра", "уточни", "сделай", "знать");
            try
            {
                return WeatherService.GetForecastAsync(city).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log("Ошибка погоды: " + ex.Message);
                return "Не получилось получить прогноз погоды. Проверьте интернет.";
            }
        }

        // ---------- Звук и громкость ----------
        if (Has(t, "громкост", "громче", "тише", "звук", "звуком", "мут"))
        {
            if (Has(t, "выключ", "отключ", "убер", "без звука", "замути", "мутн"))
            {
                SystemController.ToggleMute(true);
                return "Звук выключен.";
            }
            if (Has(t, "включи", "включ", "верн", "восстанов", "размути"))
            {
                SystemController.ToggleMute(false);
                return "Звук включен.";
            }
            if (Has(t, "громче"))
            {
                var v = SystemController.ChangeVolume(+10);
                return $"Громкость {v}%.";
            }
            if (Has(t, "тише"))
            {
                var v = SystemController.ChangeVolume(-10);
                return $"Громкость {v}%.";
            }
            if (Has(t, "максимум", "полная громкость"))
            {
                var v = SystemController.SetVolumePercent(100);
                return $"Громкость {v}%.";
            }
            if (Has(t, "минимум", "ноль"))
            {
                var v = SystemController.SetVolumePercent(0);
                return "Громкость 0%.";
            }
            var percent = ParseNumber(t);
            if (percent is int p)
                return $"Громкость {SystemController.SetVolumePercent(p)}%.";
        }

        // ---------- Работа с файлами ----------
        if (Has(t, "файл"))
        {
            var query = Strip(t, "открой", "откройте", "открыть", "найди", "найти", "запусти", "покажи", "где", "файл", "файлик");
            var path = SystemController.FindFile(query);
            if (path != null)
            {
                SystemController.LaunchApp(path);
                _log($"Открываю файл: {path}");
                return "Открываю файл.";
            }
            return $"Не нашла файл по названию «{query}».";
        }

        // ---------- Поиск в интернете ----------
        if (Has(t, "поищи", "найди", "погугли", "загугли", "поиск", "поговори"))
        {
            var query = Strip(t, "поищи", "найди", "погугли", "загугли", "поиск", "поищи мне",
                "в интернете", "в паутине", "в сети", "онлайн", "через интернет");
            if (query.Length == 0) return "Что искать?";
            SystemController.OpenWebSearch(query);
            return $"Ищу в интернете: {query}.";
        }

        // ---------- Папки ----------
        if (Has(t, "папк") && Has(t, "открой", "откройте", "открыть", "запусти", "покажи"))
        {
            var folder = SystemController.FindFolderByKeyword(t);
            if (folder != null && Directory.Exists(folder))
            {
                SystemController.LaunchApp(folder);
                return $"Открываю папку {Path.GetFileName(folder)}.";
            }
            return "Не нашла такую папку.";
        }

        // ---------- Управление браузером ----------
        if (Has(t, "закрой браузер", "закрыть браузер", "заверши браузер", "убей браузер"))
        {
            _pending = () =>
            {
                var closed = SystemController.CloseBrowser();
                _log($"Браузеров закрыто: {closed}");
            };
            return "Закрыть браузер? Несохранённые вкладки будут потеряны. Скажите «да» или «нет».";
        }
        if (Has(t, "вкладк") && (Has(t, "нов") || Has(t, "создай") || Has(t, "открой") || Has(t, "открыть") || Has(t, "поставь")))
        {
            SystemController.BrowserNewTab();
            return "Открываю новую вкладку.";
        }
        if (Has(t, "закрой вкладк", "закрыть вкладк"))
        {
            SystemController.BrowserCloseTab();
            return "Закрываю вкладку.";
        }
        if (Has(t, "адресная строка", "адрес строка"))
        {
            SystemController.BrowserAddressBar();
            return "Перехожу в адресную строку.";
        }
        if (Has(t, "обнови страницу", "перезагрузи страницу", "обнови вкладку", "перезагрузи вкладку", "обнови сайт"))
        {
            SystemController.BrowserRefresh();
            return "Обновляю страницу.";
        }
        if (Has(t, "назад") && Has(t, "страниц", "вкладк", "браузер", "сайт"))
        {
            SystemController.BrowserBack();
            return "Возвращаюсь назад.";
        }
        if (Has(t, "вперед", "вперёд") && Has(t, "страниц", "вкладк", "браузер", "сайт"))
        {
            SystemController.BrowserForward();
            return "Иду вперёд.";
        }

        // ---------- Закрыть окно ----------
        if (Has(t, "закрой", "закройте", "закрыть"))
        {
            if (Has(t, "окно", "программу", "приложение", "всё", "все", "все окна"))
            {
                SystemController.CloseActiveWindow();
                return "Закрываю активное окно.";
            }
        }

        // ---------- Приложения и сайты ----------
        if (Has(t, "открой", "откройте", "открыть", "запусти", "запусти", "запустить", "включи приложение", "покажи"))
        {
            foreach (var (keys, launch) in Apps)
                if (keys.Any(k => t.Contains(k)))
                {
                    launch();
                    return "Открываю.";
                }
        }

        // ---------- Таймеры и напоминания (до команд питания!) ----------
        if (Has(t, "таймер", "напомни", "напомнить", "будильник") ||
            (Has(t, "через") && Has(t, "выключ", "перезагруз", "рестарт")))
        {
            if (Has(t, "отмени", "отмена", "убери", "сброс", "хватит", "стоп"))
            {
                var cancelled = _timers.CancelAll();
                return cancelled > 0
                    ? $"Отменила активных таймеров: {cancelled}."
                    : "Активных таймеров нет.";
            }

            var count = ParseNumber(t);
            if (count is int n && n > 0)
            {
                var (delay, unit) = ParseDelay(t, n);

                // Таймер на выключение/перезагрузку — настоящая отложенная команда ОС
                if (Has(t, "выключ", "перезагруз", "рестарт"))
                {
                    int seconds = Math.Max(10, (int)delay.TotalSeconds);
                    bool restart = Has(t, "перезагруз", "рестарт");
                    if (restart) SystemController.Restart(seconds);
                    else SystemController.Shutdown(seconds);
                    var what = restart ? "перезагружен" : "выключен";
                    return $"Хорошо, компьютер будет {what} через {FormatSpan(n, unit)}. " +
                           "Скажите «отмени выключение», чтобы отменить.";
                }

                var note = Strip(t, "таймер", "таймеры", "напомни", "напомнить",
                    "напоминание", "будильник", "поставь", "поставить", "через", "сделай");
                var label = note.Length > 0 ? note : FormatSpan(n, unit);
                _timers.Start(delay, label);
                _log($"Таймер запущен: {label} — через {FormatSpan(n, unit)}");
                return note.Length > 0
                    ? $"Напомню через {FormatSpan(n, unit)}: {note}."
                    : $"Таймер запущен: через {FormatSpan(n, unit)}.";
            }

            return "На сколько поставить таймер? Скажите, например: «таймер на десять минут».";
        }

        // ---------- Питание (после команд со звуком!) ----------
        if (Has(t, "перезагруз", "рестарт", "reboot"))
        {
            SystemController.Restart();
            return "Компьютер будет перезагружен через минуту. Скажите «отмени перезагрузку», чтобы отменить.";
        }
        if (Has(t, "заверши сеанс", "выйди из системы", "разлогин", "выйти из профиля"))
        {
            SystemController.LogOff();
            return "Завершаю сеанс пользователя.";
        }
        if (Has(t, "спящий режим", "усни", "спать компьютер", "засыпай"))
        {
            SystemController.Sleep();
            return "Перевожу компьютер в спящий режим.";
        }
        if (Has(t, "выключ", "заверши работа", "завершение работы", "shutdown", "выруб", "shutdown компьютер"))
        {
            SystemController.Shutdown();
            return "Компьютер будет выключен через минуту. Скажите «отмени выключение», чтобы отменить.";
        }
        if (Has(t, "заблокир", "блокируй", "запри компьютер", "подзамри"))
        {
            SystemController.Lock();
            return "Компьютер заблокирован.";
        }

        return null; // команда не распознана
    }

    // ---------- Вспомогательные методы ----------

    private static bool Has(string text, params string[] keys) =>
        keys.Any(k => text.Contains(k, StringComparison.Ordinal));

    /// <summary>Определяет единицу времени для таймера по ключевым словам фразы.</summary>
    private static (TimeSpan Delay, string Unit) ParseDelay(string t, int n)
    {
        if (Has(t, "секунд", "сек")) return (TimeSpan.FromSeconds(n), "сек");
        if (Has(t, "час")) return (TimeSpan.FromHours(n), "час");
        return (TimeSpan.FromMinutes(n), "мин");
    }

    private static string FormatSpan(int n, string unit) => unit switch
    {
        "сек" => $"{n} {Plural(n, "секунду", "секунды", "секунд")}",
        "час" => $"{n} {Plural(n, "час", "часа", "часов")}",
        _ => $"{n} {Plural(n, "минуту", "минуты", "минут")}"
    };

    /// <summary>Русское склонение: «1 минуту», «5 минут», «2 минуты».</summary>
    private static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }

    /// <summary>Числа словами: «сто» → 100, «пятьдесят пять» → 55 и т.д.</summary>
    private static readonly Dictionary<string, int> NumberWords = new()
    {
        ["ноль"] = 0, ["один"] = 1, ["два"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5,
        ["шесть"] = 6, ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10,
        ["пятнадцать"] = 15, ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40,
        ["пятьдесят"] = 50, ["шестьдесят"] = 60, ["семьдесят"] = 70, ["восемьдесят"] = 80,
        ["девяносто"] = 90, ["сто"] = 100, ["полсотни"] = 50
    };

    /// <summary>Пытается найти в фразе число — цифрой или словами. Возвращает null, если числа нет.</summary>
    private static int? ParseNumber(string normalizedText)
    {
        var digit = Regex.Match(normalizedText, @"\b(\d{1,3})\b");
        if (digit.Success && int.TryParse(digit.Groups[1].Value, out var fromDigits))
            return fromDigits;

        int total = 0, current = 0;
        bool found = false;
        foreach (var w in normalizedText.Split(' '))
        {
            if (!NumberWords.TryGetValue(w, out var v)) continue;
            found = true;
            if (v == 100)
            {
                total += (current == 0 ? 1 : current) * 100;
                current = 0;
            }
            else if (v >= 10) { total += current; current = v; }
            else { current += v; }
        }
        total += current;
        return found ? total : null;
    }

    private static string Normalize(string text)
    {
        var lowered = text.ToLowerInvariant().Replace('ё', 'е');
        var words = Regex.Split(lowered, @"[\s,.!?;:()\[\]""'-]+")
                          .Where(w => w.Length > 0);
        return string.Join(' ', words);
    }

    /// <summary>Убирает из фразы служебные слова и ключевые слова команды, оставляя суть (например, тему поиска).</summary>
    private static string Strip(string normalizedText, params string[] triggers)
    {
        var words = normalizedText.Split(' ')
            .Where(w => w.Length > 1
                        && !StopWords.Contains(w)
                        && !triggers.Contains(w));
        return string.Join(' ', words);
    }
}
