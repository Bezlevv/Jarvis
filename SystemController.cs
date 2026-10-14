using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Jarvis;

/// <summary>Низкоуровневые операции над компьютером: громкость, приложения, питание, файлы, интернет.</summary>
public static class SystemController
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    // ---------- Приложения и сайты ----------

    public static void LaunchApp(string fileName)
    {
        Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
    }

    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void OpenWebSearch(string query)
    {
        OpenUrl("https://yandex.ru/search/?text=" + Uri.EscapeDataString(query));
    }

    public static void CloseActiveWindow()
    {
        System.Windows.Forms.SendKeys.SendWait("%{F4}"); // Alt+F4 для активного окна
    }

    // ---------- Управление браузером (команды клавиатуры в активном окне) ----------

    public static void BrowserRefresh() => System.Windows.Forms.SendKeys.SendWait("{F5}");
    public static void BrowserNewTab() => System.Windows.Forms.SendKeys.SendWait("^t");
    public static void BrowserCloseTab() => System.Windows.Forms.SendKeys.SendWait("^w");
    public static void BrowserBack() => System.Windows.Forms.SendKeys.SendWait("%{LEFT}");
    public static void BrowserForward() => System.Windows.Forms.SendKeys.SendWait("%{RIGHT}");
    public static void BrowserAddressBar() => System.Windows.Forms.SendKeys.SendWait("^l");

    /// <summary>Принудительно закрывает все окна браузеров (Chrome, Edge, Firefox, Opera, Brave).</summary>
    public static int CloseBrowser()
    {
        string[] browsers = { "chrome", "msedge", "firefox", "opera", "brave" };
        int closed = 0;
        foreach (var name in browsers)
            foreach (var p in Process.GetProcessesByName(name))
                try { p.Kill(); p.WaitForExit(3000); closed++; } catch { /* нет прав или уже закрыт */ }
        return closed;
    }

    // ---------- Громкость (Core Audio) ----------

    private static (AudioEndpointVolume Volume, MMDevice Device) GetDefaultVolume()
    {
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return (device.AudioEndpointVolume, device);
    }

    public static int GetVolumePercent()
    {
        var (volume, device) = GetDefaultVolume();
        using (volume) using (device)
            return (int)Math.Round(volume.MasterVolumeLevelScalar * 100);
    }

    public static int SetVolumePercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var (volume, device) = GetDefaultVolume();
        using (volume) using (device)
        {
            volume.MasterVolumeLevelScalar = percent / 100f;
            if (volume.Mute && percent > 0) volume.Mute = false;
        }
        return percent;
    }

    public static int ChangeVolume(int deltaPercent) =>
        SetVolumePercent(GetVolumePercent() + deltaPercent);

    public static bool ToggleMute(bool? mute = null)
    {
        var (volume, device) = GetDefaultVolume();
        using (volume) using (device)
        {
            volume.Mute = mute ?? !volume.Mute;
            return volume.Mute;
        }
    }

    // ---------- Питание и блокировка ----------

    public static void Shutdown(int seconds = 60) =>
        Process.Start("shutdown.exe", $"/s /t {seconds} /c \"Jarvis: выключение компьютера\"");

    public static void Restart(int seconds = 60) =>
        Process.Start("shutdown.exe", $"/r /t {seconds} /c \"Jarvis: перезагрузка компьютера\"");

    public static void CancelShutdown() => Process.Start("shutdown.exe", "/a");

    public static void LogOff() => Process.Start("shutdown.exe", "/l");

    public static void Sleep() => Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");

    public static void Lock() => LockWorkStation();

    // ---------- Папки и файлы ----------

    public static readonly IReadOnlyList<(string[] Keys, string Path)> Folders = new[]
    {
        (new[] { "рабочий стол", "рабочем столе", "рабочего стола" }, Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
        (new[] { "документ", "документы", "документах", "доки" }, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        (new[] { "загрузк", "скачанн", "скачать" }, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
        (new[] { "музык", "музыкальная" }, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        (new[] { "картин", "изображени", "фотографи" }, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
        (new[] { "видео", "видеозаписи" }, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        (new[] { "пользовател", "домашн", "профиль" }, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    };

    public static string? FindFolderByKeyword(string normalizedText)
    {
        foreach (var (keys, path) in Folders)
            if (keys.Any(k => normalizedText.Contains(k)))
                return path;
        return null;
    }

    /// <summary>Ищет файл по названию в стандартных папках пользователя. Возвращает полный путь или null.</summary>
    public static string? FindFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var roots = Folders.Select(f => f.Path).Distinct().ToArray();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        string? best = null;

        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", options); }
            catch { continue; }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (Path.GetFileNameWithoutExtension(fileName).Equals(name, StringComparison.OrdinalIgnoreCase))
                        return file; // точное совпадение — сразу берём
                    best ??= file;
                }
            }
        }
        return best;
    }
}
