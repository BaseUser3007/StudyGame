using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Площадь, которую объект занимает на земле, — по Collider2D префаба
/// (на корне или дочернем объекте). Без коллайдера объект — точка в своей позиции.
/// Прямоугольник задан смещением от позиции объекта, в тайлах.
/// </summary>
public static class ObjectFootprint
{
    private static readonly Dictionary<ResourceObject, Rect> Cache = new Dictionary<ResourceObject, Rect>();

    public static Rect Get(ResourceObject prefab)
    {
        if (prefab == null) return Rect.zero;

        if (!Cache.TryGetValue(prefab, out Rect footprint))
            Cache[prefab] = footprint = Calculate(prefab);

        return footprint;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static Rect Calculate(ResourceObject prefab)
    {
        Collider2D collider = prefab.GetComponentInChildren<Collider2D>(true);
        if (collider == null) return Rect.zero;

        // Префаб не на сцене — Collider2D.bounds у него пустые, считаем по параметрам формы
        List<Vector2> localPoints = GetLocalPoints(collider);
        if (localPoints == null)
        {
            Debug.LogWarning($"ObjectFootprint: {prefab.name} — {collider.GetType().Name} не поддерживается, " +
                             "используйте Box/Circle/Capsule/Polygon Collider 2D", prefab);
            return Rect.zero;
        }

        Vector3 origin = prefab.transform.position;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        foreach (Vector2 local in localPoints)
        {
            Vector2 offset = collider.transform.TransformPoint(local) - origin;
            min = Vector2.Min(min, offset);
            max = Vector2.Max(max, offset);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// Углы формы коллайдера в его локальных координатах.
    /// </summary>
    private static List<Vector2> GetLocalPoints(Collider2D collider)
    {
        switch (collider)
        {
            case BoxCollider2D box:
                return Corners(box.offset, box.size * 0.5f + Vector2.one * box.edgeRadius);

            case CircleCollider2D circle:
                return Corners(circle.offset, Vector2.one * circle.radius);

            case CapsuleCollider2D capsule:
                return Corners(capsule.offset, capsule.size * 0.5f);

            case PolygonCollider2D polygon:
                var points = new List<Vector2>();
                for (int i = 0; i < polygon.pathCount; i++)
                    foreach (Vector2 point in polygon.GetPath(i))
                        points.Add(point + polygon.offset);
                return points.Count > 0 ? points : null;

            default:
                return null;
        }
    }

    private static List<Vector2> Corners(Vector2 center, Vector2 extents)
    {
        return new List<Vector2>
        {
            center + new Vector2(-extents.x, -extents.y),
            center + new Vector2(-extents.x, extents.y),
            center + new Vector2(extents.x, -extents.y),
            center + new Vector2(extents.x, extents.y)
        };
    }
}
