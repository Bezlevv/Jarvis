using System.Text.Json;

namespace Jarvis;

/// <summary>Прогноз погоды через бесплатный API Open-Meteo (без ключей, нужен интернет).</summary>
public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private static readonly Dictionary<int, string> Codes = new()
    {
        [0] = "ясно", [1] = "малооблачно", [2] = "переменная облачность", [3] = "пасмурно",
        [45] = "туман", [48] = "изморозь",
        [51] = "морось", [53] = "морось", [55] = "сильная морось",
        [56] = "ледяная морось", [57] = "ледяная морось",
        [61] = "небольшой дождь", [63] = "дождь", [65] = "сильный дождь",
        [66] = "ледяной дождь", [67] = "ледяной дождь",
        [71] = "небольшой снег", [73] = "снег", [75] = "сильный снег", [77] = "снежные зерна",
        [80] = "ливень", [81] = "ливень", [82] = "сильный ливень",
        [85] = "снегопад", [86] = "сильный снегопад",
        [95] = "гроза", [96] = "гроза с градом", [99] = "сильная гроза с градом"
    };

    /// <summary>Краткий прогноз для города. Город можно писать в любой падежной форме.</summary>
    public static async Task<string> GetForecastAsync(string city)
    {
        city = city.Trim();
        if (city.Length == 0) city = "Москва";

        // Поиск города (с обрезкой окончаний: «петербурге» → «петербург»)
        (double Lat, double Lon, string Name)? place = null;
        var probe = city;
        while (probe.Length >= 3 && place == null)
        {
            place = await TryGeocodeAsync(probe);
            probe = probe[..^1];
        }
        if (place == null)
            return $"Не нашла город «{city}».";

        var p = place.Value;
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                  $"&longitude={p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                  "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m" +
                  "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  "&timezone=auto&forecast_days=2";

        using var resp = await Http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var daily = root.GetProperty("daily");

        string desc = Describe(cur.GetProperty("weather_code").GetInt32());
        int temp = (int)Math.Round(cur.GetProperty("temperature_2m").GetDouble());
        int feels = (int)Math.Round(cur.GetProperty("apparent_temperature").GetDouble());
        int humidity = cur.GetProperty("relative_humidity_2m").GetInt32();
        int wind = (int)Math.Round(cur.GetProperty("wind_speed_10m").GetDouble());

        int tomorrowMax = (int)Math.Round(daily.GetProperty("temperature_2m_max")[1].GetDouble());
        int tomorrowMin = (int)Math.Round(daily.GetProperty("temperature_2m_min")[1].GetDouble());
        var pop = daily.GetProperty("precipitation_probability_max")[1];
        string popText = pop.ValueKind == JsonValueKind.Null || pop.ValueKind == JsonValueKind.Undefined
            ? "" : $", осадки {pop.GetInt32()} процентов";

        return $"В городе {p.Name} сейчас {desc}, {Sign(temp)} градусов, ощущается как {Sign(feels)}, " +
               $"влажность {humidity} процентов, ветер {wind} метров в секунду. " +
               $"Завтра ночью {Sign(tomorrowMin)}, днём до {Sign(tomorrowMax)}{popText}.";
    }

    private static string Sign(int t) => t > 0 ? $"плюс {t}" : t < 0 ? $"минус {Math.Abs(t)}" : "ноль";

    private static string Describe(int code) => Codes.GetValueOrDefault(code, "неизвестная погода");

    private static async Task<(double Lat, double Lon, string Name)?> TryGeocodeAsync(string name)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search" +
                  $"?name={Uri.EscapeDataString(name)}&count=1&language=ru&format=json";
        using var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            return null;

        var r = results[0];
        return (r.GetProperty("latitude").GetDouble(),
                r.GetProperty("longitude").GetDouble(),
                r.GetProperty("name").GetString() ?? name);
    }
}
