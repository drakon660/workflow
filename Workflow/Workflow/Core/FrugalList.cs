using System.Collections;

namespace Workflow.Core;

/// <summary>
/// Memory-efficient list optimized for 0-1 items, but supports growing to any size.
/// - 0 items: No allocation
/// - 1 item: Stores single value directly (no array overhead)
/// - 2+ items: Uses array that grows by doubling when needed
/// </summary>
public class FrugalList<T> : IReadOnlyList<T>
{
    private T _singleItem;
    private T[] _multiItems;
    private bool _hasSingleItem;

    public int Count { get; private set; }

    public T SingleItem => _hasSingleItem ? _singleItem : default;

    public void Add(T item)
    {
        switch (Count)
        {
            case 0:
                _singleItem = item; // Store as single value
                _hasSingleItem = true;
                break;
            case 1:
                _multiItems = new T[2]; // Start with capacity of 2
                _multiItems[0] = _singleItem;
                _multiItems[1] = item;
                _hasSingleItem = false;
                break;
            default:
                if (Count >= _multiItems.Length)
                {
                    Array.Resize(ref _multiItems, _multiItems.Length * 2);
                }
                _multiItems[Count] = item;
                break;
        }

        Count++;
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (Count == 1)
        {
            yield return _singleItem;
        }
        else if (Count > 1)
        {
            for (int i = 0; i < Count; i++)
            {
                yield return _multiItems[i];
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. Count is {Count}.");
            }

            if (Count == 1)
            {
                return _singleItem;
            }

            return _multiItems[index];
        }
    }
}