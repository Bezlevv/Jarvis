using System.Text.Json;

namespace Jarvis;

/// <summary>
/// Движок распознавания речи на базе Vosk (офлайн, русский язык).
/// Захватывает звук с микрофона через NAudio и возвращает текст финальных фраз.
/// </summary>
public sealed class VoiceEngine : IDisposable
{
    private readonly Vosk.Model _model;
    private readonly Vosk.VoskRecognizer _recognizer;
    private readonly Vosk.VoskRecognizer _wakeRec;
    private NAudio.Wave.WaveIn? _mic;

    /// <summary>Промежуточный (неоконченный) текст распознавания.</summary>
    public event Action<string>? PartialResult;

    /// <summary>Финальный текст сказанной фразы.</summary>
    public event Action<string>? FinalResult;

    /// <summary>Произнесено слово пробуждения «Джарвис».</summary>
    public event Action? WakeDetected;

    private volatile bool _awake;
    private readonly Queue<short[]> _recent = new();
    private const int RecentBuffers = 12; // ~0.6 сек звука «до пробуждения» — чтобы не терять начало фразы

    /// <summary>Ассистент пробуждён и слушает команду.</summary>
    public bool Awake
    {
        get => _awake;
        set
        {
            _awake = value;
            if (!value) { _recognizer.Reset(); _recent.Clear(); }
        }
    }

    public bool IsRunning => _mic != null;

    public VoiceEngine(string modelPath)
    {
        Vosk.Vosk.SetLogLevel(-1); // -1 = отключить логи Vosk полностью
        _model = new Vosk.Model(modelPath);
        _recognizer = new Vosk.VoskRecognizer(_model, 16000f);

        // Отдельный лёгкий распознаватель для слова пробуждения:
        // слушаем его результаты отдельно и сравниваем с «Джарвис» нечётко
        // (см. IsWakeWord) — так имя ловится надёжнее.
        _wakeRec = new Vosk.VoskRecognizer(_model, 16000f);
    }

    public void Start()
    {
        if (_mic != null) return;
        _recognizer.Reset();
        _wakeRec.Reset();
        _awake = false;
        _recent.Clear();

        DebugLog.Write($"СТАРТ: устройств записи {NAudio.Wave.WaveIn.DeviceCount}, " +
                       $"формат 16000 Гц, устройство по умолчанию (DeviceNumber=0)");

        _mic = new NAudio.Wave.WaveIn
        {
            WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1), // формат, который понимает Vosk
            BufferMilliseconds = 50,
            DeviceNumber = 0 // устройство записи по умолчанию
        };
        _mic.DataAvailable += OnDataAvailable;
        _mic.StartRecording();
    }

    public void Stop()
    {
        if (_mic == null) return;
        _mic.DataAvailable -= OnDataAvailable;
        try { _mic.StopRecording(); } catch { /* микрофон мог быть недоступен */ }
        _mic.Dispose();
        _mic = null;
        _recognizer.Reset();
    }

    private int _bytesTotal;
    private int _maxLevel;
    private int _buffers;

    private void OnDataAvailable(object? sender, NAudio.Wave.WaveInEventArgs e)
    {
        var samples = new short[e.BytesRecorded / 2];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        // Диагностика: общий объём звука и максимальный уровень (0 = тишина/нет доступа к микрофону)
        _bytesTotal += e.BytesRecorded;
        foreach (var s in samples)
        {
            int a = Math.Abs(s);
            if (a > _maxLevel) _maxLevel = a;
        }
        if (++_buffers % 100 == 0) // примерно раз в 5 секунд
        {
            DebugLog.Write($"аудио: всего {_bytesTotal} байт, уровень за 5 сек: {_maxLevel} (0 = тишина)");
            _maxLevel = 0;
        }

        // Спим — слушаем только слово пробуждения
        if (!_awake)
        {
            _recent.Enqueue(samples);
            while (_recent.Count > RecentBuffers) _recent.Dequeue();

            string wakeText;
            if (_wakeRec.AcceptWaveform(samples, samples.Length))
                wakeText = ExtractText(_wakeRec.Result());
            else
                wakeText = ExtractText(_wakeRec.PartialResult());

            if (IsWakeWord(wakeText))
            {
                DebugLog.Write("ПРОБУЖДЕНИЕ: " + wakeText);
                _awake = true;
                // Прогоняем последние ~0.6 сек через распознаватель команд:
                // сказанное вместе с именем («джарвис громкость...») не потеряется
                foreach (var buf in _recent)
                    _recognizer.AcceptWaveform(buf, buf.Length);
                _recent.Clear();
                WakeDetected?.Invoke();
            }
            return;
        }

        // Пробуждены — распознаём полную фразу
        if (_recognizer.AcceptWaveform(samples, samples.Length))
        {
            var json = _recognizer.Result();
            DebugLog.Write("FINAL JSON: " + json);
            FinalResult?.Invoke(ExtractText(json));
        }
        else
            PartialResult?.Invoke(ExtractText(_recognizer.PartialResult()));
    }

    private static string ExtractText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var text)
                ? text.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ----- Нечёткое распознавание слова пробуждения -----

    private static readonly string[] WakeVariants =
        { "джарвис", "джервис", "джарвас", "джервас", "jarvis", "jarvas",
          "дарвиз", "дарвис", "жарвис", "чарвис", "шарвис", "джеврис", "джервес", "джарвиш" };

    private const float WakeThreshold = 0.5f; // похожесть слова на «Джарвис» для пробуждения
    private string _lastWakeHypo = "";

    /// <summary>Есть ли в тексте слово, похожее на «Джарвис» (с учётом искажений распознавания).</summary>
    private bool IsWakeWord(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Диагностика: пишем каждую новую гипотезу имени, чтобы тюнить порог
        if (text != _lastWakeHypo)
        {
            _lastWakeHypo = text;
            DebugLog.Write($"гипотеза имени: [{text}]");
        }

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length < 4) continue;
            foreach (var variant in WakeVariants)
                if (Similarity(word, variant) >= WakeThreshold)
                    return true;
        }
        return false;
    }

    /// <summary>Похожесть слов 0..1 по расстоянию Левенштейна.</summary>
    private static float Similarity(string a, string b)
    {
        int d = Levenshtein(a, b);
        return 1f - (float)d / Math.Max(a.Length, b.Length);
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    public void Dispose()
    {
        Stop();
        _recognizer.Dispose();
        _model.Dispose();
    }
}
