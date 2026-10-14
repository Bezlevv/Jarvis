using System.Collections.Concurrent;

namespace Jarvis;

/// <summary>Таймеры: «таймер на 10 минут» / «напомни через час» — по истечении сигнал и голосовое напоминание.</summary>
public sealed class TimerManager
{
    private readonly ConcurrentDictionary<Guid, (System.Threading.Timer Timer, string Label, DateTime Due)> _active = new();

    /// <summary>Таймер сработал.</summary>
    public event Action<string>? Fired;

    /// <summary>Список таймеров изменился (запущен/сработал/отменён).</summary>
    public event Action? Changed;

    public string ActiveText => _active.IsEmpty
        ? "нет"
        : string.Join(", ", _active.Values.OrderBy(v => v.Due).Select(v => v.Label));

    public void Start(TimeSpan delay, string label)
    {
        var id = Guid.NewGuid();
        System.Threading.Timer? timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            if (_active.TryRemove(id, out var entry))
                entry.Timer.Dispose();
            Fired?.Invoke(label);
            Changed?.Invoke();
        }, null, delay, Timeout.InfiniteTimeSpan);

        _active[id] = (timer, label, DateTime.Now + delay);
        Changed?.Invoke();
    }

    /// <summary>Отменяет все активные таймеры. Возвращает число отменённых.</summary>
    public int CancelAll()
    {
        int count = 0;
        foreach (var key in _active.Keys.ToArray())
            if (_active.TryRemove(key, out var entry))
            {
                entry.Timer.Dispose();
                count++;
            }
        if (count > 0) Changed?.Invoke();
        return count;
    }
}
