using System.Speech.Synthesis;

namespace Jarvis;

/// <summary>
/// Голосовой синтез (TTS) через встроенный движок Windows SAPI.
/// Использует русский голос, если он установлен в системе.
/// </summary>
public sealed class Speaker : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public Speaker()
    {
        var ru = _synth.GetInstalledVoices()
                        .FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("ru"));
        if (ru != null)
            _synth.SelectVoice(ru.VoiceInfo.Name);
        _synth.Rate = 1;
        _synth.Volume = 100;
    }

    /// <summary>Говорить вслух (можно выключить в интерфейсе).</summary>
    public bool Enabled { get; set; } = true;

    public void Speak(string text)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            _synth.SpeakAsyncCancelAll(); // не копим очередь, если сказали новую фразу
            _synth.SpeakAsync(text);
        }
        catch { /* голосовой пакет может отсутствовать — не роняем приложение */ }
    }

    public void Dispose() => _synth.Dispose();
}
