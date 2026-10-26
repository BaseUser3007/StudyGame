using UnityEngine;

/// <summary>
/// Сортировка спрайтов по высоте на карте: чем выше (больше Y) объект, тем меньше
/// его sortingOrder — то, что ниже на экране, рисуется поверх.
/// Диапазон задаётся по границам карты в Level сразу после генерации.
/// </summary>
public static class DepthSorting
{
    /// <summary>Порядок у нижнего края карты.</summary>
    public const int FrontOrder = 1000;
    /// <summary>Порядок у верхнего края карты.</summary>
    public const int BackOrder = 100;
    /// <summary>Всё, что должно быть поверх любых объектов (туман войны).</summary>
    public const int OverlayOrder = 2000;
    /// <summary>Всплывающие надписи (цифры урона): выше любых объектов, ниже тумана.</summary>
    public const int FloatingTextOrder = 1500;
    /// <summary>Снаряды в полёте (стрелы): над объектами мира, под всплывающими надписями и туманом.</summary>
    public const int ProjectileOrder = 1400;

    private static float _minWorldY;
    private static float _maxWorldY;
    private static bool _configured;

    public static void Configure(float minWorldY, float maxWorldY)
    {
        _minWorldY = Mathf.Min(minWorldY, maxWorldY);
        _maxWorldY = Mathf.Max(minWorldY, maxWorldY);
        _configured = true;
    }

    public static int GetOrder(float worldY)
    {
        if (!_configured || Mathf.Approximately(_minWorldY, _maxWorldY))
            return (FrontOrder + BackOrder) / 2;

        float t = Mathf.InverseLerp(_minWorldY, _maxWorldY, worldY);
        return Mathf.RoundToInt(Mathf.Lerp(FrontOrder, BackOrder, t));
    }
}
