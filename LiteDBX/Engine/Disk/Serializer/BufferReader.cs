using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Read multiple array segment as a single linear segment - Forward Only
/// </summary>
internal class BufferReader : IDisposable
{
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private readonly IEnumerator<BufferSlice> _source;
    private readonly bool _utcDate;

    private BufferSlice _current;
    private int _currentPosition; // position in _current

    public BufferReader(byte[] buffer, bool utcDate = false)
        : this(new BufferSlice(buffer, 0, buffer.Length), utcDate) { }

    public BufferReader(BufferSlice buffer, bool utcDate = false)
    {
        _source = null;
        _utcDate = utcDate;

        _current = buffer;
    }

    public BufferReader(IEnumerable<BufferSlice> source, bool utcDate = false)
    {
        _source = source.GetEnumerator();
        _utcDate = utcDate;

        _source.MoveNext();
        _current = _source.Current;
    }

    /// <summary>
    /// Current global cursor position
    /// </summary>
    public int Position { get; private set; }

    /// <summary>
    /// Indicate position are at end of last source array segment
    /// </summary>
    public bool IsEOF { get; private set; }

    public void Dispose()
    {
        _source?.Dispose();
    }

    private static byte[] EnsurePooledBufferCapacity(byte[] buffer, int bytesWritten, int requiredLength)
    {
        if (requiredLength <= buffer.Length)
        {
            return buffer;
        }

        var expanded = _bufferPool.Rent(Math.Max(requiredLength, buffer.Length * 2));

        Buffer.BlockCopy(buffer, 0, expanded, 0, bytesWritten);
        _bufferPool.Return(buffer, true);

        return expanded;
    }

    private static decimal CreateDecimal(int lo, int mid, int hi, int flags)
    {
        return new decimal(lo, mid, hi, (flags & int.MinValue) != 0, unchecked((byte)((uint)flags >> 16)));
    }

    #region Basic Read

    /// <summary>
    /// Move forward in current segment. If array segment finishes, open next segment
    /// Returns true if moved to another segment - returns false if continues in the same segment
    /// </summary>
    private bool MoveForward(int count)
    {
        // do not move forward if source finish
        if (IsEOF)
        {
            return false;
        }

        ENSURE(_currentPosition + count <= _current.Count, "forward is only for current segment");

        _currentPosition += count;
        Position += count;

        // request new source array if _current all consumed
        if (_currentPosition == _current.Count)
        {
            if (_source == null || !_source.MoveNext())
            {
                IsEOF = true;
            }
            else
            {
                _current = _source.Current;
                _currentPosition = 0;
            }

            return true;
        }

        return false;
    }

    private void EnsureCurrentSegment()
    {
        if (_currentPosition >= _current.Count && !IsEOF)
        {
            MoveForward(0);
        }
    }

    private void Advance(int count)
    {
        var remaining = count;

        while (remaining > 0)
        {
            EnsureCurrentSegment();

            if (IsEOF)
            {
                break;
            }

            var bytesLeft = _current.Count - _currentPosition;
            var bytesToSkip = Math.Min(remaining, bytesLeft);

            MoveForward(bytesToSkip);

            remaining -= bytesToSkip;
        }

        ENSURE(remaining == 0, "current value must fit inside defined buffer");
    }

    private void ReadTo(Span<byte> destination)
    {
        var remaining = destination.Length;
        var destinationOffset = 0;

        while (remaining > 0)
        {
            EnsureCurrentSegment();

            if (IsEOF)
            {
                break;
            }

            var bytesLeft = _current.Count - _currentPosition;
            var bytesToCopy = Math.Min(remaining, bytesLeft);

            _current.AsSpan(_currentPosition, bytesToCopy)
                .CopyTo(destination.Slice(destinationOffset, bytesToCopy));

            MoveForward(bytesToCopy);

            destinationOffset += bytesToCopy;
            remaining -= bytesToCopy;
        }

        ENSURE(remaining == 0, "current value must fit inside defined buffer");
    }

    /// <summary>
    /// Read bytes from source and copy into buffer. Return how many bytes was read
    /// </summary>
    public int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        if (buffer == null)
        {
            Advance(count);
            return count;
        }

        ReadTo(buffer.AsSpan(offset, count));

        return count;
    }

    /// <summary>
    /// Skip bytes (same as Read but with no array copy)
    /// </summary>
    public int Skip(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        Advance(count);
        return count;
    }

    /// <summary>
    /// Consume all data source until finish
    /// </summary>
    public void Consume()
    {
        if (_source != null)
        {
            while (_source.MoveNext()) { }
        }
    }

    #endregion

    #region Read String

    /// <summary>
    /// Read string with fixed size
    /// </summary>
    public string ReadString(int count)
    {
        string value;

        // if fits in current segment, use inner array - otherwise copy from multiples segments
        if (_currentPosition + count <= _current.Count)
        {
            value = StringEncoding.UTF8.GetString(_current.Array, _current.Offset + _currentPosition, count);

            MoveForward(count);
        }
        else
        {
            // rent a buffer to be re-usable
            var buffer = _bufferPool.Rent(count);

            Read(buffer, 0, count);

            value = StringEncoding.UTF8.GetString(buffer, 0, count);

            _bufferPool.Return(buffer, true);
        }

        return value;
    }

    /// <summary>
    /// Reading string until find \0 at end
    /// </summary>
    public string ReadCString()
    {
        // first try read CString in current segment
        if (TryReadCStringCurrentSegment(out var value))
        {
            return value;
        }

        var buffer = _bufferPool.Rent(Math.Max(_current.Count - _currentPosition, 32));
        var length = 0;

        try
        {
            while (true)
            {
                EnsureCurrentSegment();
                ENSURE(!IsEOF, "cstring must end with null terminator");

                var span = _current.AsSpan(_currentPosition, _current.Count - _currentPosition);
                var terminatorIndex = span.IndexOf((byte)0x00);
                var count = terminatorIndex >= 0 ? terminatorIndex : span.Length;

                if (count > 0)
                {
                    buffer = EnsurePooledBufferCapacity(buffer, length, length + count);
                    span.Slice(0, count).CopyTo(buffer.AsSpan(length, count));
                    MoveForward(count);
                    length += count;
                }

                if (terminatorIndex >= 0)
                {
                    MoveForward(1); // +1 to '\0'

                    return StringEncoding.UTF8.GetString(buffer, 0, length);
                }
            }
        }
        finally
        {
            _bufferPool.Return(buffer, true);
        }
    }

    /// <summary>
    /// Try read CString in current segment avoind read byte-to-byte over segments
    /// </summary>
    private bool TryReadCStringCurrentSegment(out string value)
    {
        var pos = _currentPosition;
        var count = 0;

        while (pos < _current.Count)
        {
            if (_current[pos] == 0x00)
            {
                value = StringEncoding.UTF8.GetString(_current.Array, _current.Offset + _currentPosition, count);
                MoveForward(count + 1); // +1 means '\0'	

                return true;
            }

            count++;
            pos++;
        }

        value = null;

        return false;
    }

    #endregion

    #region Read Numbers

    public int ReadInt32()
    {
        const int size = 4;

        if (_currentPosition + size <= _current.Count)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(_current.AsSpan(_currentPosition, size));

            MoveForward(size);

            return value;
        }

        Span<byte> buffer = stackalloc byte[size];

        ReadTo(buffer);

        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public long ReadInt64()
    {
        const int size = 8;

        if (_currentPosition + size <= _current.Count)
        {
            var value = BinaryPrimitives.ReadInt64LittleEndian(_current.AsSpan(_currentPosition, size));

            MoveForward(size);

            return value;
        }

        Span<byte> buffer = stackalloc byte[size];

        ReadTo(buffer);

        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    public uint ReadUInt32()
    {
        const int size = 4;

        if (_currentPosition + size <= _current.Count)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_current.AsSpan(_currentPosition, size));

            MoveForward(size);

            return value;
        }

        Span<byte> buffer = stackalloc byte[size];

        ReadTo(buffer);

        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public double ReadDouble()
    {
        const int size = 8;

        if (_currentPosition + size <= _current.Count)
        {
            var bits = BinaryPrimitives.ReadInt64LittleEndian(_current.AsSpan(_currentPosition, size));

            MoveForward(size);

            return BitConverter.Int64BitsToDouble(bits);
        }

        Span<byte> buffer = stackalloc byte[size];

        ReadTo(buffer);

        var value = BinaryPrimitives.ReadInt64LittleEndian(buffer);

        return BitConverter.Int64BitsToDouble(value);
    }

    public decimal ReadDecimal()
    {
        var a = ReadInt32();
        var b = ReadInt32();
        var c = ReadInt32();
        var d = ReadInt32();

        return CreateDecimal(a, b, c, d);
    }

    #endregion

    #region Complex Types

    /// <summary>
    /// Read DateTime as UTC ticks (not BSON format)
    /// </summary>
    public DateTime ReadDateTime()
    {
        var date = new DateTime(ReadInt64(), DateTimeKind.Utc);

        return _utcDate ? date.ToLocalTime() : date;
    }

    /// <summary>
    /// Read Guid as 16 bytes array
    /// </summary>
    public Guid ReadGuid()
    {
        Guid value;

        if (_currentPosition + 16 <= _current.Count)
        {
#if NETSTANDARD2_0
            value = _current.ReadGuid(_currentPosition);
#else
            value = new Guid(_current.AsSpan(_currentPosition, 16));
#endif

            MoveForward(16);
        }
        else
        {
#if NETSTANDARD2_0
            // can't use _tempoBuffer because Guid validate 16 bytes array length
            value = new Guid(ReadBytes(16));
#else
            Span<byte> buffer = stackalloc byte[16];

            ReadTo(buffer);

            value = new Guid(buffer);
#endif
        }

        return value;
    }

    /// <summary>
    /// Write ObjectId as 12 bytes array
    /// </summary>
    public ObjectId ReadObjectId()
    {
        ObjectId value;

        if (_currentPosition + 12 <= _current.Count)
        {
            value = new ObjectId(_current.Array, _current.Offset + _currentPosition);

            MoveForward(12);
        }
        else
        {
            var buffer = _bufferPool.Rent(12);

            Read(buffer, 0, 12);

            value = new ObjectId(buffer);

            _bufferPool.Return(buffer, true);
        }

        return value;
    }

    /// <summary>
    /// Write a boolean as 1 byte (0 or 1)
    /// </summary>
    public bool ReadBoolean()
    {
        var value = _current[_currentPosition] != 0;
        MoveForward(1);

        return value;
    }

    /// <summary>
    /// Write single byte
    /// </summary>
    public byte ReadByte()
    {
        var value = _current[_currentPosition];
        MoveForward(1);

        return value;
    }

    /// <summary>
    /// Write PageAddress as PageID, Index
    /// </summary>
    internal PageAddress ReadPageAddress()
    {
        return new PageAddress(ReadUInt32(), ReadByte());
    }

    /// <summary>
    /// Read byte array - not great because need create new array instance
    /// </summary>
    public byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        Read(buffer, 0, count);

        return buffer;
    }

    /// <summary>
    /// Read single IndexKey (BsonValue) from buffer. Use +1 length only for string/binary
    /// </summary>
    public BsonValue ReadIndexKey()
    {
        var type = (BsonType)ReadByte();

        switch (type)
        {
            case BsonType.Null: return BsonValue.Null;

            case BsonType.Int32: return ReadInt32();
            case BsonType.Int64: return ReadInt64();
            case BsonType.Double: return ReadDouble();
            case BsonType.Decimal: return ReadDecimal();

            // Use +1 byte only for length
            case BsonType.String: return ReadString(ReadByte());

            case BsonType.Document: return ReadDocument().GetValue();
            case BsonType.Array: return ReadArray().GetValue();

            // Use +1 byte only for length
            case BsonType.Binary: return ReadBytes(ReadByte());
            case BsonType.ObjectId: return ReadObjectId();
            case BsonType.Guid: return ReadGuid();

            case BsonType.Boolean: return ReadBoolean();
            case BsonType.DateTime: return ReadDateTime();

            case BsonType.MinValue: return BsonValue.MinValue;
            case BsonType.MaxValue: return BsonValue.MaxValue;

            default: throw new NotImplementedException();
        }
    }

    #endregion

    #region BsonDocument as SPECS

    /// <summary>
    /// Read a BsonDocument from reader
    /// </summary>
    public Result<BsonDocument> ReadDocument(HashSet<string> fields = null)
    {
        var doc = fields == null || fields.Count == 0 ? new BsonDocument() : new BsonDocument(fields.Count);

        try
        {
            var length = ReadInt32();
            var end = Position + length - 5;
            var selectedFields = fields == null || fields.Count == 0 ? null : fields;
            var selectedCount = 0;

            while (Position < end && (selectedFields == null || selectedCount < selectedFields.Count))
            {
                var value = ReadElement(selectedFields, out var name);

                // null value means are not selected field
                if (value != null)
                {
                    var isNewField = !doc.ContainsKey(name);
                    doc[name] = value;

                    if (selectedFields != null && isNewField)
                    {
                        selectedCount++;
                    }
                }
            }

            MoveForward(1); // skip \0 ** can read disk here!

            return doc;
        }
        catch (Exception ex)
        {
            return new Result<BsonDocument>(doc, ex);
        }
    }

    /// <summary>
    /// Read an BsonArray from reader
    /// </summary>
    public Result<BsonArray> ReadArray()
    {
        var arr = new BsonArray();

        try
        {
            var length = ReadInt32();
            var end = Position + length - 5;

            while (Position < end)
            {
                var value = ReadElement(null, out _);
                arr.Add(value);
            }

            MoveForward(1); // skip \0

            return arr;
        }
        catch (Exception ex)
        {
            return new Result<BsonArray>(arr, ex);
        }
    }

    /// <summary>
    /// Reads an element (key-value) from an reader
    /// </summary>
    private BsonValue ReadElement(HashSet<string> remaining, out string name)
    {
        var type = ReadByte();
        name = ReadCString();

        // check if need skip this element
        if (remaining != null && !remaining.Contains(name))
        {
            // define skip length according type
            var length =
                type == 0x0A || type == 0xFF || type == 0x7F ? 0 : // Null, MinValue, MaxValue
                type == 0x08 ? 1 : // Boolean
                type == 0x10 ? 4 : // Int
                type == 0x01 || type == 0x12 || type == 0x09 ? 8 : // Double, Int64, DateTime
                type == 0x07 ? 12 : // ObjectId
                type == 0x13 ? 16 : // Decimal
                type == 0x02 ? ReadInt32() : // String
                type == 0x05 ? ReadInt32() + 1 : // Binary (+1 for subtype)
                type == 0x03 || type == 0x04 ? ReadInt32() - 4 : 0; // Document, Array (-4 to Length + zero)

            if (length > 0)
            {
                Skip(length);
            }

            return null;
        }

        if (type == 0x01) // Double
        {
            return ReadDouble();
        }

        if (type == 0x02) // String
        {
            var length = ReadInt32();
            var value = ReadString(length - 1);
            MoveForward(1); // read '\0'

            return value;
        }

        if (type == 0x03) // Document
        {
            return ReadDocument().GetValue();
        }

        if (type == 0x04) // Array
        {
            return ReadArray().GetValue();
        }

        if (type == 0x05) // Binary
        {
            var length = ReadInt32();
            var subType = ReadByte();

            switch (subType)
            {
                case 0x00:
                    return ReadBytes(length);
                case 0x04:
                    if (length == 16)
                    {
                        return ReadGuid();
                    }

#if NETSTANDARD2_0
                    return new Guid(ReadBytes(length));
#else
                    return new Guid(ReadBytes(length).AsSpan());
#endif
            }
        }
        else if (type == 0x07) // ObjectId
        {
            return ReadObjectId();
        }
        else if (type == 0x08) // Boolean
        {
            return ReadBoolean();
        }
        else if (type == 0x09) // DateTime
        {
            var ts = ReadInt64();

            // catch specific values for MaxValue / MinValue #19
            if (ts == 253402300800000)
            {
                return DateTime.MaxValue;
            }

            if (ts == -62135596800000)
            {
                return DateTime.MinValue;
            }

            var date = BsonValue.UnixEpoch.AddMilliseconds(ts);

            return _utcDate ? date : date.ToLocalTime();
        }
        else if (type == 0x0A) // Null
        {
            return BsonValue.Null;
        }
        else if (type == 0x10) // Int32
        {
            return ReadInt32();
        }
        else if (type == 0x12) // Int64
        {
            return ReadInt64();
        }
        else if (type == 0x13) // Decimal
        {
            return ReadDecimal();
        }
        else if (type == 0xFF) // MinKey
        {
            return BsonValue.MinValue;
        }
        else if (type == 0x7F) // MaxKey
        {
            return BsonValue.MaxValue;
        }

        throw new NotSupportedException("BSON type not supported");
    }

    #endregion
}