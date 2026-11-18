namespace FormulaParser;

public sealed class HybridSet<T>
{
    private const int Threshold = 10;

    private readonly IEqualityComparer<T> _comparer;
    private T[] _items = new T[Threshold];
    private HashSet<T>? _hashSet;
    private int _count;

    public HybridSet()
        : this(null)
    {
    }

    public HybridSet(IEqualityComparer<T>? comparer)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public int Count => _count;

    public bool Add(T item)
    {
        if (_hashSet != null)
        {
            if (!_hashSet.Add(item))
                return false;

            _count++;
            return true;
        }

        for (var i = 0; i < _count; i++)
        {
            if (_comparer.Equals(_items[i], item))
                return false;
        }

        if (_count < Threshold)
        {
            _items[_count++] = item;
            return true;
        }

        UpgradeToHashSet();
        _hashSet!.Add(item);
        _count++;
        return true;
    }

    public bool Contains(T item)
    {
        if (_hashSet != null)
            return _hashSet.Contains(item);

        for (var i = 0; i < _count; i++)
        {
            if (_comparer.Equals(_items[i], item))
                return true;
        }

        return false;
    }

    public T[] ToArray()
    {
        if (_count == 0)
            return [];

        var result = new T[_count];
        if (_hashSet != null)
        {
            _hashSet.CopyTo(result);
            return result;
        }

        Array.Copy(_items, result, _count);
        return result;
    }

    private void UpgradeToHashSet()
    {
        _hashSet = new HashSet<T>(Threshold * 2, _comparer);

        for (var i = 0; i < _count; i++)
            _hashSet.Add(_items[i]);

        _items = [];
    }
}
