using System;
using System.Collections.Generic;

/// <summary>Что произошло, когда игрок встал на тайл площадки с плитами.</summary>
public enum PlateStepResult
{
    /// <summary>На тайле нет плиты.</summary>
    NotAPlate,
    /// <summary>Верная плита нажата, но не последняя.</summary>
    Pressed,
    /// <summary>Нажата последняя плита — головоломка решена.</summary>
    Completed,
    /// <summary>Не та плита: все плиты отжаты, начинать с наименьшего номера.</summary>
    Reset,
    /// <summary>Плита уже нажата — ничего не меняется (можно ходить по нажатым).</summary>
    AlreadyPressed,
    /// <summary>Головоломка уже решена — плиты больше не реагируют.</summary>
    AlreadySolved
}

/// <summary>
/// Логика плит (T16), без Unity: наступать по возрастанию номеров. Верная плита (наименьший номер среди
/// ненажатых) нажимается, нажатая — не реагирует, любая другая — сбрасывает все. Все нажаты — решено.
/// </summary>
public sealed class PlatesSequence
{
    private readonly int[] _numbers;
    private readonly bool[] _pressed;

    public int Count => _numbers.Length;
    public int PressedCount { get; private set; }
    public bool IsCompleted => PressedCount == _numbers.Length && _numbers.Length > 0;

    /// <summary>Номер, который нажимать следующим; −1 — всё нажато.</summary>
    public int NextNumber
    {
        get
        {
            int next = -1;
            for (int i = 0; i < _numbers.Length; i++)
                if (!_pressed[i] && (next < 0 || _numbers[i] < next))
                    next = _numbers[i];
            return next;
        }
    }

    /// <param name="numbers">Номера плит по индексу — все разные.</param>
    public PlatesSequence(IReadOnlyList<int> numbers)
    {
        if (numbers == null) throw new ArgumentNullException(nameof(numbers));

        _numbers = new int[numbers.Count];
        var seen = new HashSet<int>();
        for (int i = 0; i < numbers.Count; i++)
        {
            if (!seen.Add(numbers[i]))
                throw new ArgumentException($"PlatesSequence: duplicate plate number {numbers[i]}", nameof(numbers));
            _numbers[i] = numbers[i];
        }
        _pressed = new bool[_numbers.Length];
    }

    // ─── Public API ───────────────────────────────────────────

    public int GetNumber(int index) => _numbers[index];

    public bool IsPressed(int index) => index >= 0 && index < _pressed.Length && _pressed[index];

    /// <summary>Игрок встал на плиту index (−1 — не на плиту).</summary>
    public PlateStepResult Step(int index)
    {
        if (index < 0 || index >= _numbers.Length) return PlateStepResult.NotAPlate;
        if (IsCompleted) return PlateStepResult.AlreadySolved;
        if (_pressed[index]) return PlateStepResult.AlreadyPressed;

        if (_numbers[index] != NextNumber)
        {
            Reset();
            return PlateStepResult.Reset;
        }

        _pressed[index] = true;
        PressedCount++;
        return IsCompleted ? PlateStepResult.Completed : PlateStepResult.Pressed;
    }

    /// <summary>Отжать все плиты.</summary>
    public void Reset()
    {
        Array.Clear(_pressed, 0, _pressed.Length);
        PressedCount = 0;
    }
}
