using System.Collections.Generic;
using UnityEngine;

public class ObjectPool
{
    private readonly Dictionary<string, Queue<ResourceObject>> _pools
        = new Dictionary<string, Queue<ResourceObject>>();

    private readonly Transform _container;

    public ObjectPool(Transform container)
    {
        Debug.Assert(container != null, "ObjectPool: container is null");
        _container = container;
    }

    // ─── Get / Return ─────────────────────────────────────────

    public ResourceObject Get(ResourceObject prefab, Vector3 position)
    {
        string key = GetKey(prefab);

        ResourceObject obj = _pools.TryGetValue(key, out Queue<ResourceObject> pool)
            && pool.Count > 0
            ? pool.Dequeue()
            : CreateObject(prefab);

        obj.transform.position = position;
        obj.gameObject.SetActive(true);
        obj.ResetState();
        return obj;
    }

    public void Return(ResourceObject obj)
    {
        if (obj == null) return;

        string key = GetKey(obj);

        obj.gameObject.SetActive(false);
        obj.transform.SetParent(_container);

        if (!_pools.ContainsKey(key))
            _pools[key] = new Queue<ResourceObject>();

        _pools[key].Enqueue(obj);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private ResourceObject CreateObject(ResourceObject prefab)
    {
        ResourceObject obj = Object.Instantiate(prefab, _container);
        obj.gameObject.SetActive(false);
        return obj;
    }

    private string GetKey(ResourceObject obj) => obj.GetType().Name;
}