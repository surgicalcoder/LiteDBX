using System;
using System.Collections;
using System.Collections.Generic;

namespace LiteDbX;

public class BsonArray : BsonValue, IList<BsonValue>
{
    private int _length;

    public BsonArray()
        : this(0) { }

    protected BsonArray(int capacity)
        : base(BsonType.Array, capacity > 0 ? new List<BsonValue>(capacity) : new List<BsonValue>()) { }

    public BsonArray(List<BsonValue> array)
        : this()
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        AddRange(array);
    }

    public BsonArray(params BsonValue[] array)
        : this()
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        AddRange(array);
    }

    public BsonArray(IEnumerable<BsonValue> items)
        : this()
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        AddRange(items);
    }

    public new IList<BsonValue> RawValue => (IList<BsonValue>)base.RawValue;

    public override BsonValue this[int index]
    {
        get => RawValue[index];
        set => RawValue[index] = value ?? Null;
    }

    public int Count => RawValue.Count;

    public bool IsReadOnly => false;

    public void Add(BsonValue item)
    {
        RawValue.Add(item ?? Null);
    }

    public void Clear()
    {
        RawValue.Clear();
    }

    public bool Contains(BsonValue item)
    {
        return RawValue.Contains(item ?? Null);
    }

    public void CopyTo(BsonValue[] array, int arrayIndex)
    {
        RawValue.CopyTo(array, arrayIndex);
    }

    public IEnumerator<BsonValue> GetEnumerator()
    {
        return RawValue.GetEnumerator();
    }

    public int IndexOf(BsonValue item)
    {
        return RawValue.IndexOf(item ?? Null);
    }

    public void Insert(int index, BsonValue item)
    {
        RawValue.Insert(index, item ?? Null);
    }

    public bool Remove(BsonValue item)
    {
        return RawValue.Remove(item);
    }

    public void RemoveAt(int index)
    {
        RawValue.RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        foreach (var value in RawValue)
        {
            yield return value;
        }
    }

    public void AddRange<TCollection>(TCollection collection)
        where TCollection : ICollection<BsonValue>
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        var list = (List<BsonValue>)base.RawValue;

        var listEmptySpace = list.Capacity - list.Count;

        if (listEmptySpace < collection.Count)
        {
            list.Capacity += collection.Count;
        }

        foreach (var bsonValue in collection)
        {
            list.Add(bsonValue ?? Null);
        }
    }

    public void AddRange(IEnumerable<BsonValue> items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        foreach (var item in items)
        {
            Add(item ?? Null);
        }
    }

    public override int CompareTo(BsonValue other)
    {
        // if types are different, returns sort type order
        if (other.Type != BsonType.Array)
        {
            return Type.CompareTo(other.Type);
        }

        var otherArray = other.AsArray;

        var result = 0;
        var i = 0;
        var stop = Math.Min(Count, otherArray.Count);

        // compare each element
        for (; 0 == result && i < stop; i++)
        {
            result = this[i].CompareTo(otherArray[i]);
        }

        if (result != 0)
        {
            return result;
        }

        if (i == Count)
        {
            return i == otherArray.Count ? 0 : -1;
        }

        return 1;
    }

    internal override int GetBytesCount(bool recalc)
    {
        if (!recalc && _length > 0)
        {
            return _length;
        }

        var length = 5;
        var array = RawValue;

        for (var i = 0; i < array.Count; i++)
        {
            length += GetBytesCountElement(i.ToString(), array[i]);
        }

        return _length = length;
    }
}