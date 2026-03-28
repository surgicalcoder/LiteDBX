using System;
using System.Collections.Generic;

namespace LiteDbX.Engine;

internal class SortKey : BsonArray
{
    private readonly int[] _orders;

    private static int[] CopyOrders(IReadOnlyList<int> orders)
    {
        var copy = new int[orders.Count];

        for (var i = 0; i < orders.Count; i++)
        {
            copy[i] = orders[i];
        }

        return copy;
    }

    private SortKey(IReadOnlyList<BsonValue> values, IReadOnlyList<int> orders)
        : base(values?.Count ?? throw new ArgumentNullException(nameof(values)))
    {
        if (orders == null) throw new ArgumentNullException(nameof(orders));

        _orders = orders as int[] ?? CopyOrders(orders);

        if (_orders.Length != values.Count)
        {
            throw new ArgumentException("Orders length must match values length", nameof(orders));
        }

        for (var i = 0; i < values.Count; i++)
        {
            Add(values[i] ?? BsonValue.Null);
        }
    }

    public override int CompareTo(BsonValue other)
    {
        return CompareTo(other, Collation.Binary);
    }

    public override int CompareTo(BsonValue other, Collation collation)
    {
        if (other is SortKey sortKey)
        {
            return CompareArrays(this, sortKey, _orders, collation);
        }

        if (other is BsonArray array)
        {
            return CompareArrays(this, array, _orders, collation);
        }

        return base.CompareTo(other, collation);
    }

    internal static int Compare(BsonValue left, BsonValue right, IReadOnlyList<int> orders, Collation collation)
    {
        if (left is SortKey leftSortKey)
        {
            return leftSortKey.CompareTo(right, collation);
        }

        if (right is SortKey rightSortKey)
        {
            var result = rightSortKey.CompareTo(left, collation);

            return result == 0 ? 0 : -result;
        }

        if (orders != null && orders.Count > 1 && left is BsonArray leftArray && right is BsonArray rightArray)
        {
            return CompareArrays(leftArray, rightArray, orders, collation);
        }

        return left.CompareTo(right, collation);
    }

    public static SortKey FromValues(IReadOnlyList<BsonValue> values, IReadOnlyList<int> orders)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        if (orders == null) throw new ArgumentNullException(nameof(orders));

        return new SortKey(values, orders);
    }

    public static SortKey FromBsonValue(BsonValue value, IReadOnlyList<int> orders)
    {
        if (value is SortKey sortKey) return sortKey;

        if (value is BsonArray array)
        {
            return new SortKey(array, orders);
        }

        return new SortKey(new[] { value }, orders);
    }

    private SortKey(BsonArray array, IReadOnlyList<int> orders)
        : base(array?.Count ?? throw new ArgumentNullException(nameof(array)))
    {
        if (orders == null) throw new ArgumentNullException(nameof(orders));

        _orders = orders as int[] ?? CopyOrders(orders);

        if (_orders.Length != array.Count)
        {
            throw new ArgumentException("Orders length must match values length", nameof(orders));
        }

        for (var i = 0; i < array.Count; i++)
        {
            Add(array[i] ?? BsonValue.Null);
        }
    }

    private static int CompareArrays(BsonArray left, BsonArray right, IReadOnlyList<int> orders, Collation collation)
    {
        var length = Math.Min(left.Count, right.Count);

        for (var i = 0; i < length; i++)
        {
            var result = left[i].CompareTo(right[i], collation);

            if (result == 0) continue;

            return orders[i] == Query.Descending ? -result : result;
        }

        if (left.Count == right.Count) return 0;

        return left.Count < right.Count ? -1 : 1;
    }
}
