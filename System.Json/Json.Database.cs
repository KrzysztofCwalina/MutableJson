using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// TODO: the records are large. they should be accessed by ref

namespace System.Json
{
    [DebuggerTypeProxy(typeof(DebuggerRecords))]
    partial class Json
    {
        Memory<byte> _records;
        Memory<byte> _data;

        Json(Memory<byte> records) {
            _records = records;
        }

        Header ResizeRecords() {
            if(_records.IsEmpty) {
                _records = new byte[16 * Record.SizeInBytes];
                var header = Header;
                header.SetType();
                header.RecordsLength = Record.SizeInBytes;
                header.DataLength = 0;
                return header;
            }
            else {
                throw new NotImplementedException();
            }
        }

        void ResizeData() {
            var size = _data.Span.Length;
            if (size == 0) size = 128;
            var larger = new byte[size * 2];
            _data.Span.CopyTo(larger);
            _data = larger;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowArgumentOutOfRange(string name) => throw new ArgumentOutOfRangeException(name);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Record GetRecordByOffset(int recordOffset) {
            Debug.Assert(_records.Length >= recordOffset + Record.SizeInBytes);
            return new Record(_records.Span.Slice(recordOffset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Span<byte> GetSpanByIndex(int index) {
            if (!TryGetRecordByIndex(index, out var record)) {
                ThrowArgumentOutOfRange(nameof(index));
            }
            return record;
        }

        Header Header {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var records = _records.Span;
                Debug.Assert(records.Length >= Record.SizeInBytes);
                return new Header(records);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TryGetRecordByIndex(int index, out Span<byte> record) {
            var offset = Record.IndexToOffset(index);
            if(_records.Length >= offset + Record.SizeInBytes) {
                record = _records.Span.Slice(offset, Record.SizeInBytes);
                return true;
            }
            record = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int FindInstanceOffset(int instanceId) {
            if (!TryGetRecordByIndex(instanceId, out var record)) {
                ThrowArgumentOutOfRange(nameof(instanceId));
            }
            return BinaryPrimitives.ReadInt32LittleEndian(record);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetObjectOffset(int instanceId, int offset) {
            if (!TryGetRecordByIndex(instanceId, out var record)) {
                ThrowArgumentOutOfRange(nameof(instanceId));
            }
            BinaryPrimitives.WriteInt32LittleEndian(record, offset);
        }

        int FindInstancePropertyOffset(int instanceId, string propertyName = null) {
            var instanceOffset = FindInstanceOffset(instanceId);
            return FindPropertyOffset(instanceOffset, propertyName);
        }

        int FindPropertyOffset(int recordOffset, string propertyName = null) {
            if (_records.IsEmpty) return -1;
            Debug.Assert(_records.Length >= Record.SizeInBytes);

            if (propertyName == null) {
                Debug.Assert(recordOffset == 0);
                return 0; // literal record
            }

            Span<byte> stackUtf8 = stackalloc byte[128];
            int stackUtf8Length = -1;
            byte[] allocatedUtf8 = null;
            if (!Utf8.TryToUtf8(ref stackUtf8, propertyName, out stackUtf8Length)) {
                allocatedUtf8 = Encoding.UTF8.GetBytes(propertyName);
            }

            while (recordOffset >= 0) {
                Record record = GetRecordByOffset(recordOffset);
                var name = record.GetName(_data.Span);

                // TODO: maybe there is a way to win with the span checker to simplify this
                if (allocatedUtf8!=null) {
                    if (name.SequenceEqual(allocatedUtf8)) {
                        return recordOffset;
                    }
                }
                else {
                    if (name.SequenceEqual(stackUtf8.Slice(0, stackUtf8Length))) {
                        return recordOffset;
                    }
                }

                recordOffset = record.NextOffset;
            }
            return -1;
        }

        long GetValue(int recordOffset) {
            return BinaryPrimitives.ReadInt64LittleEndian(_records.Span.Slice(recordOffset + Record.OffsetValue));
        }
        int GetInlineStringLength(int recordOffset) {
            return BinaryPrimitives.ReadInt32LittleEndian(_records.Span.Slice(recordOffset + Record.OffsetRangeLength));
        }

        string GetJsonString(int recordOffset) {
            var type = GetRecordType(recordOffset);
            if(type == RecordType.StringLiteralInline) {
                var length = GetInlineStringLength(recordOffset);
                var utf8 = _records.Span.Slice(recordOffset + Record.OffsetRangeStart, length);
                return Encoding.UTF8.GetString(utf8.ToArray()); // TODO: this should be optimized
            }
            throw new NotImplementedException();
        }

        Span<byte> AddRecord(out int offset) {
            Header header = Header;
            offset = header.RecordsLength;
            if (offset + Record.SizeInBytes > _records.Length) {
                header = ResizeRecords();
                offset = header.RecordsLength;
            }
            var free = _records.Span.Slice(offset, Record.SizeInBytes);
            return free;
        }

        (int, int) AddString(string text) {
            Header header = Header;
            var dataLength = header.DataLength;
            var free = _data.Span.Slice(dataLength);
            if (Utf8.TryToUtf8(ref free, text, out int written)) {
                return (dataLength, written);
            }
            ResizeData();
            return AddString(text);
        }
    }
}
