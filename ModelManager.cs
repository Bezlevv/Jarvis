using System.IO.Compression;

namespace Jarvis;

/// <summary>
/// Скачивает и распаковывает офлайн-модель распознавания речи Vosk (русский язык).
/// Модель кладётся в папку models/ рядом с исполняемым файлом.
/// Защищена от обрывов: скачивание во временный файл, проверка размера,
/// при повреждении архива — автоматическая перезагрузка.
/// </summary>
public static class ModelManager
{
    private const string ModelName = "vosk-model-small-ru-0.22";
    private const string ModelUrl = "https://alphacephei.com/vosk/models/" + ModelName + ".zip";
    private const long ExpectedSize = 45_000_000; // модель ~46 МБ, ниже — загрузка точно оборвалась

    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "models", ModelName);

    /// <summary>Возвращает путь к модели, при необходимости скачивая и распаковывая её.</summary>
    public static string EnsureModel(Action<string> status)
    {
        if (IsModelComplete())
            return ModelPath;

        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        Directory.CreateDirectory(modelsDir);
        var zipPath = Path.Combine(modelsDir, ModelName + ".zip");

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!File.Exists(zipPath) || new FileInfo(zipPath).Length < ExpectedSize)
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    Download(status, zipPath);
                }

                status("Распаковываю модель...");
                ZipFile.ExtractToDirectory(zipPath, modelsDir, overwriteFiles: true);
                File.Delete(zipPath);

                if (!IsModelComplete())
                    throw new InvalidDataException("Архив распакован неполностью.");

                return ModelPath;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or HttpRequestException)
            {
                // Архив повреждён или обрыв — чистим и пробуем ещё раз
                TryDelete(zipPath);
                TryDelete(Path.Combine(modelsDir, ModelName));
                if (attempt == maxAttempts)
                    throw new IOException(
                        $"Не удалось загрузить модель за {maxAttempts} попыток. Последняя ошибка: {ex.Message}",
                        ex);
                status($"Загрузка прервалась, пробую снова ({attempt + 1}/{maxAttempts})...");
            }
        }

        throw new IOException("Не удалось загрузить модель.");
    }

    private static bool IsModelComplete() =>
        File.Exists(Path.Combine(ModelPath, "am", "final.mdl")) &&
        File.Exists(Path.Combine(ModelPath, "conf", "mfcc.conf"));

    private static void Download(Action<string> status, string zipPath)
    {
        status("Скачиваю русскую модель распознавания речи (~48 МБ)...");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var resp = http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead)
                              .GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();

        var tmpPath = zipPath + ".part";
        try
        {
            using (var fs = File.Create(tmpPath))
                resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();

            // Проверяем, что скачалось всё
            var expected = resp.Content.Headers.ContentLength;
            var actual = new FileInfo(tmpPath).Length;
            if (actual < ExpectedSize || (expected is > 0 && actual != expected))
                throw new InvalidDataException($"Скачано {actual} из {expected?.ToString() ?? "?"} байт.");

            File.Move(tmpPath, zipPath, overwrite: true);
        }
        finally
        {
            TryDelete(tmpPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { /* не мешаем повторной попытке */ }
    }
}
