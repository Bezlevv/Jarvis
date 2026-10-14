using System.Text.Json;

namespace Jarvis;

/// <summary>Пользовательская команда: фразы-триггеры → действие.</summary>
public sealed class CustomCommand
{
    public string Name { get; set; } = "";
    public List<string> Phrases { get; set; } = new();

    /// <summary>Тип действия: url | search | app | file | folder | reply</summary>
    public string Action { get; set; } = "reply";

    /// <summary>Параметр: адрес, путь, поисковый запрос или текст ответа.</summary>
    public string Value { get; set; } = "";
}

/// <summary>Хранилище своих команд в commands.json рядом с программой (редактируется вручную).</summary>
public static class CommandLibrary
{
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "commands.json");

    public static List<CustomCommand> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var example = new List<CustomCommand>
                {
                    new()
                    {
                        Name = "Мой GitHub",
                        Phrases = new List<string> { "мой гитхаб", "мой github" },
                        Action = "url",
                        Value = "https://github.com/Bezlevv"
                    },
                    new()
                    {
                        Name = "Комплимент ассистенту",
                        Phrases = new List<string> { "ты лучший", "ты классный" },
                        Action = "reply",
                        Value = "Спасибо, я стараюсь!"
                    }
                };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(
                    example, new JsonSerializerOptions { WriteIndented = true }));
                return example;
            }

            return JsonSerializer.Deserialize<List<CustomCommand>>(File.ReadAllText(FilePath))
                   ?? new List<CustomCommand>();
        }
        catch
        {
            return new List<CustomCommand>(); // повреждённый файл не должен ломать ассистента
        }
    }
}
