using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.HighPerformance;
using Framework.Text;

namespace Framework.Buffers;

public ref struct SpanWriter
{
    private readonly bool _resize;
    private byte[] _arrayToReturnToPool;
    private Span<byte> _buffer;
    private int _position;
    private int _bytesWritten;
    public int BytesWritten
    {
        get => _bytesWritten;
        private set => _bytesWritten = value;
    }

    public int Position
    {
        get => _position;
        private set
        {
            _position = value;

            if (value > _bytesWritten)
            {
                _bytesWritten = value;
            }
        }
    }

    public int Capacity => _buffer.Length;

    public ReadOnlySpan<byte> Span => _buffer[..Position];

    public Span<byte> RawBuffer => _buffer;

    /**
         * Converts the writer to a Span<byte> using a SpanOwner.
         * If the buffer was stackalloc, it will be copied to a rented buffer.
         * Otherwise the existing rented buffer is used.
         *
         * Note:
         * Do not use the SpanWriter after calling this method.
         * This method will effectively dispose of the SpanWriter and is therefore considered terminal.
         */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOwner ToSpan()
    {
        var toReturn = _arrayToReturnToPool;

        SpanOwner apo;
        if (_position == 0)
        {
            apo = new SpanOwner(_position, Array.Empty<byte>());
            if (toReturn != null)
            {
                STArrayPool<byte>.Shared.Return(toReturn);
            }
        }
        else if (toReturn != null)
        {
            apo = new SpanOwner(_position, toReturn);
        }
        else
        {
            var buffer = STArrayPool<byte>.Shared.Rent(_position);
            _buffer.CopyTo(buffer);
            apo = new SpanOwner(_position, buffer);
        }

        this = default; // Don't allow two references to the same buffer
        return apo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanWriter(Span<byte> initialBuffer, bool resize = false)
    {
        _resize = resize;
        _buffer = initialBuffer;
        _position = 0;
        BytesWritten = 0;
        _arrayToReturnToPool = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanWriter(int initialCapacity, bool resize = false)
    {
        _resize = resize;
        _arrayToReturnToPool = STArrayPool<byte>.Shared.Rent(initialCapacity);
        _buffer = _arrayToReturnToPool;
        _position = 0;
        BytesWritten = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacity)
    {
        var newSize = Math.Max(BytesWritten + additionalCapacity, _buffer.Length * 2);
        byte[] poolArray = STArrayPool<byte>.Shared.Rent(newSize);

        _buffer[..BytesWritten].CopyTo(poolArray);

        byte[] toReturn = _arrayToReturnToPool;
        _buffer = _arrayToReturnToPool = poolArray;
        if (toReturn != null)
        {
            STArrayPool<byte>.Shared.Return(toReturn);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GrowIfNeeded(int count)
    {
        if (_position + count > _buffer.Length)
        {
            if (!_resize)
            {
                throw new OutOfMemoryException();
            }

            Grow(count);
        }
    }

    public ref byte GetPinnableReference() => ref MemoryMarshal.GetReference(_buffer);

    public void EnsureCapacity(int capacity)
    {
        if (capacity > _buffer.Length)
        {
            if (!_resize)
            {
                throw new OutOfMemoryException();
            }

            Grow(capacity - BytesWritten);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Write(bool value)
    {
        GrowIfNeeded(1);
        _buffer[Position++] = *(byte*)&value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteHexStringBytes(string hexString)
    {
        int length = hexString.Length / 2;

        if ((hexString.Length % 2) == 0)
        {
            for (int i = 0; i < length; i++)
            {
                Write((byte)Convert.ToByte(hexString.Substring(i * 2, 2), 16));
            }
        }
        else
        {
            Write((byte)0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(byte value)
    {
        GrowIfNeeded(1);
        _buffer[Position++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(sbyte value)
    {
        GrowIfNeeded(1);
        _buffer[Position++] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(short value)
    {
        GrowIfNeeded(2);
        BinaryPrimitives.WriteInt16BigEndian(_buffer[_position..], value);
        Position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE(short value)
    {
        GrowIfNeeded(2);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer[_position..], value);
        Position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ushort value)
    {
        GrowIfNeeded(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer[_position..], value);
        Position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE(ushort value)
    {
        GrowIfNeeded(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_position..], value);
        Position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(int value)
    {
        GrowIfNeeded(4);
        BinaryPrimitives.WriteInt32BigEndian(_buffer[_position..], value);
        Position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE(int value)
    {
        GrowIfNeeded(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[_position..], value);
        Position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(uint value)
    {
        GrowIfNeeded(4);
        BinaryPrimitives.WriteUInt32BigEndian(_buffer[_position..], value);
        Position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE(uint value)
    {
        GrowIfNeeded(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_position..], value);
        Position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(long value)
    {
        GrowIfNeeded(8);
        BinaryPrimitives.WriteInt64BigEndian(_buffer[_position..], value);
        Position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ulong value)
    {
        GrowIfNeeded(8);
        BinaryPrimitives.WriteUInt64BigEndian(_buffer[_position..], value);
        Position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE(ulong value)
    {
        GrowIfNeeded(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer[_position..], value);
        Position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> buffer)
    {
        var count = buffer.Length;
        GrowIfNeeded(count);
        buffer.CopyTo(_buffer[_position..]);
        Position += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteVarUInt(uint val)
    {
        if (val == 0)
        {
            Write((byte)0);
            return;
        }

        while (val > 0)
        {
            Write((byte)((val & 0x7F) ^ ((val > 0x7F) ? 0x80 : 0x00)));
            val = val >> 7;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteZigZag(int val)
    {
        byte sign = (byte)(val < 0 ? 1 : 0);
        if (sign == 1)
            val++;
        val = Math.Abs(val);

        Write((byte)(((val << 1) & 0x7F) ^ ((val > 0x3F) ? 0x80 : 0x00) + sign));

        val = val >> 6;

        while (val > 0)
        {
            Write((byte)((val & 0x7F) ^ ((val > 0x7F) ? 0x80 : 0x00)));
            val = val >> 7;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString32(string str)
    {
        Write(str.Length);
        Write(str.GetBytesAscii());
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUtfString(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((byte)0);
            return;
        }

        byte[] bytes = UTF_8.GetBytes(str);
        Write((byte)bytes.Length);
        Write(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteVarIntString(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((byte)0);
            return;
        }

        byte[] bytes = ISO8859_1.GetBytes(str);
        WriteVarUInt((uint)str.Length);
        Write(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteShortString(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((ushort)0);
            return;
        }

        byte[] bytes = ISO8859_1.GetBytes(str);
        Write((ushort)bytes.Length);
        Write(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePascalString(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((byte)0);
            return;
        }

        byte[] bytes = ISO8859_1.GetBytes(str);
        Write((byte)bytes.Length);
        Write(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStringToZero(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((byte)1);
        }
        else
        {
            byte[] bytes = ISO8859_1.GetBytes(str);
            Write((byte)(bytes.Length + 1));
            Write(bytes);
        }

        Write((byte)0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteShortStringToZero(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((ushort)1);
        }
        else
        {
            byte[] bytes = ISO8859_1.GetBytes(str);
            Write((ushort)(bytes.Length + 1));
            Write(bytes);
        }

        Write((byte)0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteCString(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            Write((byte)0);
            return;
        }

        byte[] bytes = ISO8859_1.GetBytes(str);
        Write(bytes);
        Write((byte)0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUTFPascalStringBytes(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((byte)0);
            return;
        }
        Encoding defaultEnc = Encoding.Default;
        byte[] utfBytes = defaultEnc.GetBytes(str);
        byte[] isoBytes = Encoding.Convert(defaultEnc, Encoding.UTF8, utfBytes);
        Write((byte)isoBytes.Length);
        if (str.Length > 0)
        {
            Write(isoBytes);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUTFStringBytes(string str)
    {
        if (str == null || str.Length <= 0)
        {
            Write((ushort)0);
            return;
        }

        Encoding defaultEnc = Encoding.Default;
        byte[] utfBytes = defaultEnc.GetBytes(str);
        byte[] isoBytes = Encoding.Convert(defaultEnc, Encoding.UTF8, utfBytes);
        Write((ushort)isoBytes.Length);
        if (str.Length > 0)
        {
            Write(isoBytes);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAscii(char chr) => Write((byte)chr);

    public void WriteAscii(
        ref RawInterpolatedStringHandler handler)
    {
        Write(handler.Text, Encoding.ASCII);
        handler.Clear();
    }

    public void WriteAscii(
        IFormatProvider? formatProvider,
        [InterpolatedStringHandlerArgument("formatProvider")]
        ref RawInterpolatedStringHandler handler)
    {
        Write(handler.Text, Encoding.ASCII);
        handler.Clear();
    }

    public void Write(
        Encoding encoding,
        ref RawInterpolatedStringHandler handler)
    {
        Write(handler.Text, encoding);
        handler.Clear();
    }

    public void Write(
        Encoding encoding,
        IFormatProvider? formatProvider,
        [InterpolatedStringHandlerArgument("formatProvider")]
        ref RawInterpolatedStringHandler handler)
    {
        Write(handler.Text, encoding);
        handler.Clear();
    }

    public void Write(ReadOnlySpan<char> value, Encoding encoding, int fixedLength = -1)
    {
        var charLength = Math.Min(fixedLength > -1 ? fixedLength : value.Length, value.Length);
        var src = value[..charLength];

        var byteLength = encoding.GetByteLengthForEncoding();
        var byteCount = encoding.GetByteCount(src);
        if (fixedLength > src.Length)
        {
            byteCount += (fixedLength - src.Length) * byteLength;
        }

        if (byteCount == 0)
        {
            return;
        }

        GrowIfNeeded(byteCount);

        var bytesWritten = encoding.GetBytes(src, _buffer[_position..]);
        Position += bytesWritten;

        if (fixedLength > -1)
        {
            var extra = fixedLength * byteLength - bytesWritten;
            if (extra > 0)
            {
                Clear(extra);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLittleUni(string value) => Write(value, TextEncoding.UnicodeLE);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLittleUniNull(string value)
    {
        Write(value, TextEncoding.UnicodeLE);
        Write((ushort)0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLittleUni(string value, int fixedLength) => Write(value, TextEncoding.UnicodeLE, fixedLength);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBigUni(string value) => Write(value, TextEncoding.Unicode);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBigUniNull(string value)
    {
        Write(value, TextEncoding.Unicode);
        Write((ushort)0); // '\0'
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBigUni(string value, int fixedLength) => Write(value, TextEncoding.Unicode, fixedLength);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUTF8(string value) => Write(value, TextEncoding.UTF8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUTF8Null(string value)
    {
        Write(value, TextEncoding.UTF8);
        Write((byte)0); // '\0'
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAscii(string value) => Write(value, Encoding.ASCII);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAsciiNull(string value)
    {
        Write(value, Encoding.ASCII);
        Write((byte)0); // '\0'
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAscii(string value, int fixedLength) => Write(value, Encoding.ASCII, fixedLength);
    
    private static readonly Encoding ISO8859_1 = Encoding.GetEncoding("iso-8859-1");
    private static readonly Encoding UTF_8 = Encoding.GetEncoding("utf-8");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FillString(string str, int span)
    {
        GrowIfNeeded(span);
        Span<byte> buffer = stackalloc byte[span];
        buffer.Fill(0x00);
        if (!String.IsNullOrEmpty(str))
        {
            Span<byte> arr = ISO8859_1.GetBytes(str);
            arr.Slice(0, Math.Min(arr.Length, span)).CopyTo(buffer);
        }
        buffer.CopyTo(_buffer.Slice(Position, span));
        Position += span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStringBytes(string str)
    {
        if (str.Length <= 0)
            return;

        Span<byte> bytes = ISO8859_1.GetBytes(str);
        GrowIfNeeded(str.Length);
        bytes.CopyTo(_buffer.Slice(Position, str.Length));
        Position += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear(int count)
    {
        GrowIfNeeded(count);
        _buffer.Slice(_position, count).Clear();
        Position += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Fill(byte value, int count)
    {
        GrowIfNeeded(count);
        _buffer.Slice(_position, count).Fill(value);
        Position += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Seek(int offset, SeekOrigin origin)
    {
        Debug.Assert(
            origin != SeekOrigin.End || _resize || offset <= 0,
            "Attempting to seek to a position beyond capacity using SeekOrigin.End without resize"
        );

        Debug.Assert(
            origin != SeekOrigin.End || offset >= -_buffer.Length,

            "Attempting to seek to a negative position using SeekOrigin.End"
        );

        Debug.Assert(
            origin != SeekOrigin.Begin || offset >= 0,
            "Attempting to seek to a negative position using SeekOrigin.Begin"
        );

        Debug.Assert(
            origin != SeekOrigin.Begin || _resize || offset <= _buffer.Length,
            "Attempting to seek to a position beyond the capacity using SeekOrigin.Begin without resize"
        );

        Debug.Assert(
            origin != SeekOrigin.Current || _position + offset >= 0,
            "Attempting to seek to a negative position using SeekOrigin.Current"
        );

        Debug.Assert(
            origin != SeekOrigin.Current || _resize || _position + offset <= _buffer.Length,
            "Attempting to seek to a position beyond the capacity using SeekOrigin.Current without resize"
        );

        var newPosition = Math.Max(0, origin switch
        {
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => BytesWritten + offset,
            _ => offset // Begin
        });

        if (newPosition > _buffer.Length)
        {
            Grow(newPosition - _buffer.Length + 1);
        }

        return Position = newPosition;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        byte[] toReturn = _arrayToReturnToPool;
        this = default; // for safety, to avoid using pooled array if this instance is erroneously appended to again
        if (toReturn != null)
        {
            STArrayPool<byte>.Shared.Return(toReturn);
        }
    }

    public struct SpanOwner : IDisposable
    {
        private readonly int _length;
        private readonly byte[] _arrayToReturnToPool;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SpanOwner(int length, byte[] buffer)
        {
            _length = length;
            _arrayToReturnToPool = buffer;
        }

        public Span<byte> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MemoryMarshal.CreateSpan(ref _arrayToReturnToPool.DangerousGetReference(), _length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            byte[] toReturn = _arrayToReturnToPool;
            this = default;
            if (_length > 0)
            {
                STArrayPool<byte>.Shared.Return(toReturn);
            }
        }
    }
}