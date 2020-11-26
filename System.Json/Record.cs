using System.Buffers.Binary;
using System.Diagnostics;

namespace System.Json
{
    public enum RecordType : byte 
    {
        Null = 0, 
        True = 1,
        False = 2,

        Int64 = 3,
        String = 4,
        Object = 5,

        TruePropertyInline,
        FalsePropertyInline,
        NullPropertyInline,

        StringLiteralInline,

        Clear = 255,
        Header = 254,
    }

    public readonly ref struct Header
    {
        readonly Span<byte> _record;

        public Header(Span<byte> recordBytes) {
            Debug.Assert(recordBytes.Length >= Record.SizeInBytes);
            _record = recordBytes;
        }

        public void SetType() {
            _record[Record.OffsetType] = (byte)RecordType.Header;
        }

        public int RecordsLength {
            get => BinaryPrimitives.ReadInt32LittleEndian(_record.Slice(Record.OffsetRangeLength));
            set => BinaryPrimitives.WriteInt32LittleEndian(_record.Slice(Record.OffsetRangeLength), value);
        }
        public int DataLength {
            get => BinaryPrimitives.ReadInt32LittleEndian(_record.Slice(Record.OffsetRangeStart));
            set => BinaryPrimitives.WriteInt32LittleEndian(_record.Slice(Record.OffsetRangeStart), value);
        }
    }

    public readonly ref struct TypeAndNameRecord
    {
        readonly Span<byte> _record;

        public TypeAndNameRecord(Span<byte> recordBytes) {
            Debug.Assert(recordBytes.Length >= Record.SizeInBytes);
            _record = recordBytes;
        }

        public RecordType Type {
            set {
                Debug.Assert(value == RecordType.Null || value == RecordType.False || value == RecordType.True);
                _record[Record.OffsetType] = (byte)value;
            }
        }

        public ReadOnlySpan<byte> GetName() {
            Debug.Assert(
                ((RecordType)_record[Record.OffsetType]) == RecordType.Null ||
                ((RecordType)_record[Record.OffsetType]) == RecordType.False ||
                ((RecordType)_record[Record.OffsetType]) == RecordType.True
            );
            var nameBuffer = _record.Slice(Record.OffsetByteBuffer, Record.MaxByteBufferLength);
            var nameEnd = nameBuffer.IndexOf((byte)0);
            if (nameEnd >= 0) {
                nameBuffer = nameBuffer.Slice(0, nameEnd);
            }
            return nameBuffer;
        }

        public bool TrySetName(string value) {
            var nameBuffer = _record.Slice(Record.OffsetByteBuffer, Record.MaxByteBufferLength);
            if (Utf8.TryToUtf8(ref nameBuffer, value, out int written)) {
                nameBuffer.Slice(written).Fill(0);
                return true;
            }
            return false;
        }
    }

    public readonly ref struct Record {
        readonly Span<byte> _record;
        
        // int ObjectHandle; // if this record is at index X, object with instance ID X starts at ObjectHandle
        // int NextRecordIndex; // next record of the same instance

        // byte Type; // RecordType enum
        // byte _b1;
        // byte _b2;
        // byte _b3;
        // int  _b4to7;

        // int RangeStart;
        // int RangeLength;

        // long Value;

        public Record(Span<byte> recordBytes) {
            Debug.Assert(recordBytes.Length >= SizeInBytes);
            _record = recordBytes;
        }

        // Table Properties
        public const int OffsetObjectHandle = 0;
        public const int OffsetNextRecord = OffsetObjectHandle + sizeof(int);

        // Record Properties
        public const int OffsetType = OffsetNextRecord + sizeof(int);
        public const int OffsetByteBuffer = OffsetType + sizeof(RecordType);
        public const int MaxByteBufferLength = SizeInBytes - OffsetByteBuffer;

        public const int OffsetRangeStart = OffsetType + sizeof(long);
        public const int OffsetRangeLength = OffsetRangeStart + sizeof(int);
        
        public const int OffsetValue = OffsetRangeStart + sizeof(long);

        public const int SizeInBytes = OffsetValue + sizeof(long);
        public const int IndexToOffsetShift = 5;

        public const byte InlineStringMask = 0b1;

        public static int IndexToOffset(int index) => index << IndexToOffsetShift;

        internal ReadOnlySpan<byte> GetName(ReadOnlySpan<byte> stringTable) {
            var type = Type;
            if(type == RecordType.False || type == RecordType.True || type == RecordType.Null) {
                var record = new TypeAndNameRecord(_record);
                return record.GetName();
            }
            throw new NotImplementedException();
        }

        public int RangeLength {
            get => BinaryPrimitives.ReadInt32LittleEndian(_record.Slice(OffsetRangeLength));
            set => BinaryPrimitives.WriteInt32LittleEndian(_record.Slice(OffsetRangeLength), value);
        }
        public RecordType Type {
            get => (RecordType)_record[OffsetType];
            set => _record[OffsetType] = (byte)value;
        }
        public long Value {
            get => BinaryPrimitives.ReadInt64LittleEndian(_record.Slice(OffsetValue));
            set => BinaryPrimitives.WriteInt64LittleEndian(_record.Slice(OffsetValue), value);
        }

        public int NextOffset => BinaryPrimitives.ReadInt32LittleEndian(_record.Slice(OffsetNextRecord));
    }
}
