using System.IO.Compression;

namespace Jarvis;

/// <summary>
/// Скачивает и распаковывает офлайн-модель распознавания речи Vosk (русский язык).
/// Модель кладётся в папку models/ рядом с исполняемым файлом.
/// </summary>
public static class ModelManager
{
    private const string ModelName = "vosk-model-small-ru-0.22";
    private const string ModelUrl = "https://alphacephei.com/vosk/models/" + ModelName + ".zip";

    public static string ModelsRoot => Path.Combine(AppContext.BaseDirectory, "models");

    public static string ModelPath => Path.Combine(ModelsRoot, ModelName);

    /// <summary>Возвращает путь к модели, при необходимости скачивая и распаковывая её.</summary>
    public static string EnsureModel(Action<string> status)
    {
        if (Directory.Exists(ModelPath) && File.Exists(Path.Combine(ModelPath, "am", "final.mdl")))
            return ModelPath;

        Directory.CreateDirectory(ModelsRoot);
        var zipPath = Path.Combine(ModelsRoot, ModelName + ".zip");

        if (!File.Exists(zipPath))
        {
            status($"Скачиваю русскую модель распознавания речи (~48 МБ)...");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var resp = http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead)
                                  .GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            using var fs = File.Create(zipPath);
            resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();
        }

        status("Распаковываю модель...");
        ZipFile.ExtractToDirectory(zipPath, ModelsRoot, overwriteFiles: true);
        File.Delete(zipPath);

        if (!Directory.Exists(ModelPath))
            throw new DirectoryNotFoundException($"Не удалось распаковать модель в {ModelPath}");

        return ModelPath;
    }
}
