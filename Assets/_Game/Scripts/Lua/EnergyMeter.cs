using System;

/// <summary>
/// Списывает энергию во время выполнения Lua-кода: за вызовы команд и за выполненные
/// конструкции языка (их расставляет <see cref="EnergyInstrumenter"/>).
/// Когда энергии не хватает, бросает <see cref="OutOfEnergyException"/> и остаётся «пустым»
/// до следующего запуска — даже если код поймает ошибку через pcall.
/// </summary>
public class EnergyMeter
{
    public event Action OnExhausted;

    private readonly PlayerEnergy _energy;

    public bool IsExhausted { get; private set; }
    /// <summary>Причина остановки — для сообщения игроку.</summary>
    public string ExhaustedMessage { get; private set; }

    /// <summary>
    /// Запуск закончен изнутри (enterPortal, старт головоломки): любое следующее списание бросает
    /// <see cref="RunHaltedException"/>, так что pcall не даст коду продолжиться. Сбрасывается в Reset.
    /// </summary>
    public bool IsHalted { get; private set; }

    /// <summary>Текст исключения остановки (<see cref="Halt"/>); null — стандартный (enterPortal).</summary>
    public string HaltMessage { get; private set; }

    public EnergyMeter(PlayerEnergy energy)
    {
        _energy = energy;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Вызывается перед каждым запуском программы.
    /// </summary>
    public void Reset()
    {
        IsExhausted = false;
        ExhaustedMessage = null;
        IsHalted = false;
        HaltMessage = null;
    }

    /// <summary>
    /// Останавливает запуск: дальше ничего не списывается, каждое списание — исключение.
    /// message — текст исключения (его увидит pcall); null — «закончен enterPortal()».
    /// </summary>
    public void Halt(string message = null)
    {
        IsHalted = true;
        HaltMessage = message;
    }

    /// <summary>Подсветка выполняемой строки (может не быть — например, в тестах).</summary>
    public LuaLineTracker LineTracker { get; set; }

    /// <summary>Точки останова и шаги (может не быть).</summary>
    public LuaDebugger Debugger { get; set; }

    /// <summary>
    /// Отметить место выполнения и встать, если на новой строке точка останова или идёт пошаговое выполнение.
    /// Вызывают обёртки команд, <c>__energy</c> и методы списков — все точки, где код игрока платит энергию, —
    /// до списания (строка видна, даже если энергия кончилась; на паузе энергия не тратится).
    /// Обычно завершается сразу; на паузе ждёт Continue / Step / отмены ct.
    /// </summary>
    public Cysharp.Threading.Tasks.UniTask CheckpointAsync(Lua.LuaState state, System.Threading.CancellationToken ct)
    {
        if (LineTracker == null || !LineTracker.Probe(state)) return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        if (Debugger == null || !Debugger.ShouldPause(LineTracker.Chunk, LineTracker.Line))
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        return Debugger.PauseAsync(state, LineTracker.Chunk, LineTracker.Line, ct);
    }

    public void Charge(float amount, string source)
    {
        if (amount <= 0f) return;
        if (IsHalted) throw new RunHaltedException(HaltMessage);

        if (!IsExhausted && _energy.TrySpend(amount)) return;

        if (!IsExhausted)
        {
            IsExhausted = true;
            ExhaustedMessage = System.FormattableString.Invariant($"out of energy at {source}: need {amount:0.#}, have {_energy.Current:0.#}");
            OnExhausted?.Invoke();
        }

        throw new OutOfEnergyException(ExhaustedMessage);
    }
}

public class OutOfEnergyException : Exception
{
    public OutOfEnergyException(string message) : base(message) { }
}

/// <summary>
/// Запуск закончен изнутри (enterPortal, старт головоломки): код дальше не выполняется. Наследник OperationCanceledException —
/// песочница считает это остановкой, а не ошибкой кода.
/// </summary>
public class RunHaltedException : OperationCanceledException
{
    public const string HaltMessage = "the run was ended by enterPortal()";

    /// <summary>Запуск прерван стартом головоломки (T25): попытка — следующий запуск.</summary>
    public const string PuzzleMessage = "the run was interrupted: a puzzle started — the next run is its attempt";

    public RunHaltedException() : base(HaltMessage) { }

    public RunHaltedException(string message) : base(string.IsNullOrEmpty(message) ? HaltMessage : message) { }
}
