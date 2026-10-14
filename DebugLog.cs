namespace Jarvis;

/// <summary>Диагностический журнал в файл jarvis.log (рядом с exe) — для отладки распознавания.</summary>
public static class DebugLog
{
    private static readonly object Lock = new();

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "jarvis.log");

    public static void Write(string message)
    {
        lock (Lock)
        {
            try
            {
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch { /* не мешаем работе ассистента */ }
        }
    }
}
