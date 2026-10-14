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

    /// <summary>Ожидающее подтверждение действие (например, «закрыть браузер? да/нет»).</summary>
    private Action? _pending;

    public CommandProcessor(Action<string> log) => _log = log;

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

        // ---------- Приветствие и вежливость ----------
        if (Has(t, "привет", "здравствуй", "добрый день", "доброе утро", "добрый вечер", "хэй"))
            return "Привет! Я готов к работе.";
        if (Has(t, "спасибо", "благодарю"))
            return "Пожалуйста!";

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
            var match = Regex.Match(t, @"\b(\d{1,3})\b");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                return $"Громкость {SystemController.SetVolumePercent(percent)}%.";
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
        if (Has(t, "новая вкладка", "новую вкладку", "создай вкладку", "открой вкладку"))
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
