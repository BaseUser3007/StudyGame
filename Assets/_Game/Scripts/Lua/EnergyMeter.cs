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
    }

    public void Charge(float amount, string source)
    {
        if (amount <= 0f) return;

        if (!IsExhausted && _energy.TrySpend(amount)) return;

        if (!IsExhausted)
        {
            IsExhausted = true;
            ExhaustedMessage = $"out of energy at {source}: need {amount:0.#}, have {_energy.Current:0.#}";
            OnExhausted?.Invoke();
        }

        throw new OutOfEnergyException(ExhaustedMessage);
    }
}

public class OutOfEnergyException : Exception
{
    public OutOfEnergyException(string message) : base(message) { }
}
