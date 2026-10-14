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
    private NAudio.Wave.WaveIn? _mic;

    /// <summary>Промежуточный (неоконченный) текст распознавания.</summary>
    public event Action<string>? PartialResult;

    /// <summary>Финальный текст сказанной фразы.</summary>
    public event Action<string>? FinalResult;

    public bool IsRunning => _mic != null;

    public VoiceEngine(string modelPath)
    {
        Vosk.Vosk.SetLogLevel(-1); // -1 = отключить логи Vosk полностью
        _model = new Vosk.Model(modelPath);
        _recognizer = new Vosk.VoskRecognizer(_model, 16000f);
    }

    public void Start()
    {
        if (_mic != null) return;
        _recognizer.Reset();

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

    private void OnDataAvailable(object? sender, NAudio.Wave.WaveInEventArgs e)
    {
        var samples = new short[e.BytesRecorded / 2];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        if (_recognizer.AcceptWaveform(samples, samples.Length))
            FinalResult?.Invoke(ExtractText(_recognizer.Result()));
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

    public void Dispose()
    {
        Stop();
        _recognizer.Dispose();
        _model.Dispose();
    }
}
