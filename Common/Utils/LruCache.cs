using System;
using System.Collections.Generic;

namespace CustomMcLauncher.Common.Utils;

public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private sealed class LruNode
    {
        public TKey Key { get; }
        public TValue Value { get; set; }

        public LruNode(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    private readonly int _capacity;
    private readonly Action<TValue>? _onEvict;
    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<LruNode>> _map;
    private readonly LinkedList<LruNode> _list;

    public LruCache(int capacity, Action<TValue>? onEvict = null, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        _onEvict = onEvict;
        _map = new Dictionary<TKey, LinkedListNode<LruNode>>(capacity, comparer);
        _list = new LinkedList<LruNode>();
    }

    public int Count
    {
        get
        {
            lock (_lock)
                return _map.Count;
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        TValue? evicted = default;
        bool hasEvicted = false;

        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                evicted = existing.Value.Value;
                hasEvicted = true;
                _list.Remove(existing);
                existing.Value.Value = value;
                _list.AddFirst(existing);
            }
            else
            {
                if (_map.Count >= _capacity)
                {
                    var last = _list.Last;
                    if (last != null)
                    {
                        _list.RemoveLast();
                        _map.Remove(last.Value.Key);
                        evicted = last.Value.Value;
                        hasEvicted = true;
                    }
                }

                var newNode = new LinkedListNode<LruNode>(new LruNode(key, value));
                _list.AddFirst(newNode);
                _map[key] = newNode;
            }
        }

        if (hasEvicted && evicted is not null)
        {
            DisposeOrEvict(evicted);
        }
    }

    public bool Remove(TKey key)
    {
        TValue? evicted = default;
        bool removed = false;

        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _map.Remove(key);
                _list.Remove(node);
                evicted = node.Value.Value;
                removed = true;
            }
        }

        if (removed && evicted is not null)
        {
            DisposeOrEvict(evicted);
        }

        return removed;
    }

    public void Clear()
    {
        List<TValue> toEvict;
        lock (_lock)
        {
            toEvict = new List<TValue>(_map.Count);
            foreach (var node in _map.Values)
            {
                toEvict.Add(node.Value.Value);
            }
            _map.Clear();
            _list.Clear();
        }

        foreach (var item in toEvict)
        {
            if (item is not null)
                DisposeOrEvict(item);
        }
    }

    public void TrimTo(int targetCount)
    {
        if (targetCount < 0) targetCount = 0;
        List<TValue> toEvict = new();

        lock (_lock)
        {
            while (_map.Count > targetCount)
            {
                var last = _list.Last;
                if (last == null) break;
                _list.RemoveLast();
                _map.Remove(last.Value.Key);
                toEvict.Add(last.Value.Value);
            }
        }

        foreach (var item in toEvict)
        {
            if (item is not null)
                DisposeOrEvict(item);
        }
    }

    private void DisposeOrEvict(TValue item)
    {
        try
        {
            if (_onEvict != null)
            {
                _onEvict(item);
            }
            else if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // Eviction cleanup failure must never propagate
        }
    }
}
