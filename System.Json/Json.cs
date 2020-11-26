using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

// TODO: the records are large. they should be accessed by ref

namespace System.Json
{
    partial struct Records
    {
        public struct DebuggerRecords
        {
            ReadOnlyMemory<byte> _records;

            public DebuggerRecords(Records records) {
                _records = records._records;
            }

            public DebuggerRecord[] Records {
                get {
                    return new DebuggerRecord[0];
                }
            }
        }

        public struct DebuggerRecord
        {

        }
    }


    [DebuggerTypeProxy(typeof(Records.DebuggerRecords))]
    partial struct Records
    {
        Memory<byte> _records;

        private Records(Memory<byte> records) {
            _records = records;
        }

        public static Records Deserialize(byte[] bytes) {
            if (bytes.Length < Record.SizeInBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
            var records = new Records(bytes);
            return records;
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

        ReadOnlySpan<byte> StringTable => ReadOnlySpan<byte>.Empty;

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
        int GetObjectOffset(int instanceId) {
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

        // TODO: make private?
        internal int FindOffset(int instanceId, string propertyName = null) {
            if (_records.IsEmpty) return -1;
            Debug.Assert(_records.Length >= Record.SizeInBytes);

            if (propertyName == null) {
                Debug.Assert(instanceId == 0);
                return 0; // literal record
            }

            Span<byte> stackUtf8 = stackalloc byte[128];
            int stackUtf8Length = -1;
            byte[] allocatedUtf8 = null;
            if (!Utf8.TryToUtf8(ref stackUtf8, propertyName, out stackUtf8Length)) {
                allocatedUtf8 = Encoding.UTF8.GetBytes(propertyName);
            }

            int offset = GetObjectOffset(instanceId);

            while (offset >= 0) {
                Record record = GetRecordByOffset(offset);
                var name = record.GetName(StringTable);

                // TODO: maybe there is a way to win with the span checker to simplify this
                if (allocatedUtf8!=null) {
                    if (name.SequenceEqual(allocatedUtf8)) {
                        return offset;
                    }
                }
                else {
                    if (name.SequenceEqual(stackUtf8.Slice(0, stackUtf8Length))) {
                        return offset;
                    }
                }

                offset = record.NextOffset;
            }
            return -1;
        }

        internal RecordType GetRecordType(int recordOffset) {
            if (_records.IsEmpty) return RecordType.Object;
            return (RecordType)BinaryPrimitives.ReadInt32LittleEndian(_records.Span.Slice(recordOffset + Record.OffsetType));
        }

        internal long GetValue(int recordOffset) {
            return BinaryPrimitives.ReadInt64LittleEndian(_records.Span.Slice(recordOffset + Record.OffsetValue));
        }
        internal int GetInlineStringLength(int recordOffset) {
            return BinaryPrimitives.ReadInt32LittleEndian(_records.Span.Slice(recordOffset + Record.OffsetRangeLength));
        }

        internal string GetJsonString(int recordOffset) {
            var type = GetRecordType(recordOffset);
            if(type == RecordType.StringLiteralInline) {
                var length = GetInlineStringLength(recordOffset);
                var utf8 = _records.Span.Slice(recordOffset + Record.OffsetRangeStart, length);
                return Encoding.UTF8.GetString(utf8.ToArray()); // TODO: this should be optimized
            }
            throw new NotImplementedException();
        }

        internal void Set(int instanceId, string propertyName, bool value) {
            var offset = FindOffset(instanceId, propertyName);

            if (offset == -1) {
                if(_records.IsEmpty) {
                    _records = new byte[Record.SizeInBytes * 2];
                    
                    // setup header
                    var header = Header;
                    header.SetType();
                    header.RecordsLength = Record.SizeInBytes + Record.SizeInBytes;

                    // setup first record
                    var span = GetSpanByIndex(1);
                    var record = new TypeAndNameRecord(span);
                    if (!record.TrySetName(propertyName)) {
                        record.Type = value ? RecordType.TruePropertyInline : RecordType.FalsePropertyInline;
                    }
                    else {
                        throw new NotImplementedException();
                    }
                    SetObjectOffset(0, Record.SizeInBytes);
                }
            }

            throw new NotImplementedException();
        }

        internal void Set(int instanceId, string propertyName, long value) {
            throw new NotImplementedException();
        }

        internal void Set(int instanceId, string propertyName, string value) {
            throw new NotImplementedException();
        }
    }

    public class Json
    {
        const int RootInstanceId = 0;

        Records _records = new Records();

        public Json() { }

        public Json(byte[] serialized) {
            _records = Records.Deserialize(serialized);
        }

        public JsonValue Root => new JsonValue(this, RootInstanceId);

        internal RecordType GetRecordType(int instanceId) {
            int recordIndex = _records.FindOffset(instanceId);
            return _records.GetRecordType(recordIndex);
        }

        internal long ToInt64(int instanceId) {
            int recordIndex = _records.FindOffset(instanceId);
            return _records.GetValue(recordIndex);
        }

        internal string ToJsonString(int instanceId) {
            int recordIndex = _records.FindOffset(instanceId);
            return _records.GetJsonString(recordIndex);
        }

        internal void Set(int instanceId, string propertyName, long number) {
            _records.Set(instanceId, propertyName, number);
        }
        internal void Set(int instanceId, string propertyName, bool boolean) {
            _records.Set(instanceId, propertyName, boolean);
        }
        internal void Set(int instanceId, string propertyName, string text) {
            _records.Set(instanceId, propertyName, text);
        }

        internal bool GetBoolean(int instanceId, string propertyName) {
            int recordIndex = _records.FindOffset(instanceId, propertyName);
            if (recordIndex == -1) throw new KeyNotFoundException();
            var type =_records.GetRecordType(recordIndex);
            if (type == RecordType.False) return false;
            if (type == RecordType.True) return true;
            throw new InvalidCastException();
        }

        internal bool GetInt64(int instanceId, string propertyName) {
            int recordIndex = _records.FindOffset(instanceId, propertyName);
            if (recordIndex == -1) throw new KeyNotFoundException();
            var type = _records.GetRecordType(recordIndex);
            if (type == RecordType.Int64) _records.GetValue(recordIndex);
            throw new InvalidCastException();
        }

        internal string GetString(int instanceId, string propertyName) {
            int recordIndex = _records.FindOffset(instanceId, propertyName);
            if (recordIndex == -1) throw new KeyNotFoundException();
            var type = _records.GetRecordType(recordIndex);
            throw new NotImplementedException();
        }

        //internal RecordType JsonType => _records.GetType(0);

        //#region getters
        //internal bool GetBoolean(int instanceId, string name) {
        //    var index = FindRecord(instanceId, name);
        //    var record = _records[index];

        //    if (record.Type == RecordType.False) return false;
        //    if (record.Type == RecordType.True) return true;
        //    else throw new InvalidCastException();
        //}

        //internal ReadOnlySpan<byte> GetString(int instanceId, string name) {
        //    var index = FindRecord(instanceId, name);
        //    if (_records.GetType(index) == RecordType.String) return _records.AsUtf8(index);
        //    else throw new InvalidCastException();
        //}

        //internal JsonObject GetObject(int instanceId, string name) {
        //    var index = FindRecord(instanceId, name);
        //    var record = _records[index];
        //    if (record.Type == RecordType.Object) return new JsonObject(this, (int)record.Value);
        //    else throw new InvalidCastException();
        //}
        //#endregion

        //#region setters
        //public void Set(string name, string value) => Root.Set(name, value);
        //public void Set(string name, bool value) => Root.Set(name, value);
        //public void Set(string name, long value) => Root.Set(name, value);
        //public JsonObject SetObject(string name) => Root.SetObject(name);
        //#endregion

        //public string ToJsonString() {
        //    var stream = new MemoryStream();
        //    WriteTo(stream, 'u');
        //    return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        //}

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="stream"></param>
        ///// <param name="format">Use 'u' for UTF8. Use 'b' for binary</param>
        //public void WriteTo(Stream stream, StandardFormat format) {
        //    if (format.Symbol == 'u') {
        //        if (_records.Count == 0) {
        //            return;
        //        }
        //        if (_records.Count == 1 && _records[0].Name.IsEmpty) {
        //            WriteLiteral(ref _records[0], stream);
        //            return;
        //        }

        //        var options = new JsonWriterOptions();
        //        options.Indented = true;
        //        var writer = new Utf8JsonWriter(stream, options);

        //        WriteObject(writer, 0, ReadOnlySpan<byte>.Empty);
        //        writer.Flush();
        //    }
        //    else if (format.Symbol == 'b') {
        //        _records.Serialize(stream);
        //    }
        //    else throw new ArgumentOutOfRangeException(nameof(format));
        //}

        //private void WriteObject(Utf8JsonWriter writer, int index, ReadOnlySpan<byte> name) {
        //    if (name.IsEmpty) writer.WriteStartObject();
        //    else writer.WriteStartObject(name);

        //    var record = _records[index];
        //    while (true) {
        //        var propertyName = _records.GetPropertyName(index);
        //        switch (record.Type) {
        //            case RecordType.False:
        //                writer.WriteBoolean(propertyName, false);
        //                break;
        //            case RecordType.True:
        //                writer.WriteBoolean(propertyName, true);
        //                break;
        //            case RecordType.Int64:
        //                writer.WriteNumber(propertyName, record.Value);
        //                break;
        //            case RecordType.Null:
        //                writer.WriteNull(propertyName);
        //                break;
        //            case RecordType.String:
        //                var span = record.ValueToSpan();
        //                writer.WriteString(propertyName, _records.AsUtf8(index));
        //                break;
        //            case RecordType.Clear:
        //                break;
        //            case RecordType.Object:
        //                int objIndex = _records[(int)record.Value].ObjectHandle;
        //                WriteObject(writer, objIndex, propertyName);
        //                break;
        //            default: throw new NotImplementedException();
        //        }

        //        index = record.NextRecordIndex;
        //        if (index < 1) break;
        //        record = _records[index];
        //    }
        //    writer.WriteEndObject();
        //}

        //private void WriteLiteral(ref Record record, Stream stream) {
        //    byte[] payload;
        //    switch (record.Type) {
        //        case RecordType.Null:
        //            payload = Encoding.UTF8.GetBytes("null");
        //            break;
        //        case RecordType.False:
        //            payload = Encoding.UTF8.GetBytes("false");
        //            break;
        //        case RecordType.True:
        //            payload = Encoding.UTF8.GetBytes("true");
        //            break;
        //        default:
        //            throw new NotImplementedException();
        //    }
        //    stream.Write(payload, 0, payload.Length);
        //}

        //internal void SetCore(int instanceId, string name, string value) {
        //    var newString = _records.AddString(value);
        //    var newName = _records.AddString(name); // TODO (pri 0): only add name if adding a new record. Updates don't need to add name.
        //    var newRecord = new Record(instanceId, newName, newString);
        //    SetRecord(instanceId, name, ref newRecord);
        //}

        //internal void SetCore(int instanceId, string name, bool value) {
        //    var newName = _records.AddString(name);
        //    var newRecord = new Record(instanceId, newName, value ? RecordType.True : RecordType.False, 0);
        //    SetRecord(instanceId, name, ref newRecord);
        //}

        //internal void SetCore(int instanceId, string name, long value) {
        //    var newName = _records.AddString(name);
        //    var newRecord = new Record(instanceId, newName, RecordType.Int64, value);
        //    SetRecord(instanceId, name, ref newRecord);
        //}

        //internal JsonObject SetObjectCore(int instance, string name) {
        //    var newObjectId = _records.NextInstanceId;
        //    _records[0].InstanceId = newObjectId + 1;
        //    var newName = _records.AddString(name);
        //    var newRecord = new Record(instance, newName, RecordType.Object, newObjectId);
        //    SetRecord(instance, name, ref newRecord);
        //    var obj = new JsonObject(this, newObjectId);
        //    return obj;
        //}

        //void SetRecord(int instanceId, string name, ref Record newRecord) {
        //    if (_records.TryFindRecord(instanceId, name, out int index)) {
        //        var old = _records[index];
        //        if(old.Type == RecordType.Object) {
        //            throw new NotImplementedException(); // TODO: implement
        //        }
        //        newRecord.ObjectHandle = old.ObjectHandle;
        //        newRecord.NextRecordIndex = old.NextRecordIndex;
        //        _records[index] = newRecord;
        //    }
        //    else {
        //        int newIndex = AddRecord(ref newRecord);
        //        if (index != -1) {
        //            _records[index].NextRecordIndex = newIndex;
        //        }
        //    }
        //}

        ///// <returns>index of the added record</returns>
        //int AddRecord(ref Record record) {
        //    var index = _records.Add(ref record);

        //    // if totally new object
        //    if (_records[record.InstanceId].ObjectHandle == 0) {
        //        _records[record.InstanceId].ObjectHandle = _records.Count;
        //    }

        //    return index;
        //}


        //int FindRecord(int instanceId, string name) {
        //    if (_records.TryFindRecord(instanceId, name, out int index)) {
        //        return index;
        //    }
        //    throw new KeyNotFoundException();
        //}
    }

    public readonly struct JsonValue
    {
        readonly Json _json;
        readonly int _id;

        public RecordType JsonType => _json.GetRecordType(_id);

        public bool IsNull => JsonType == RecordType.Null;

        public bool ToBoolean() => JsonType == RecordType.True;

        public long ToInt64() => _json.ToInt64(_id);

        public string ToJsonString() => _json.ToJsonString(_id);

        public void Set(string propertyName, long number)
            => _json.Set(_id, propertyName, number);

        public void Set(string propertyName, bool boolean) 
            => _json.Set(_id, propertyName, boolean);
        
        public void Set(string propertyName, string text) 
            => _json.Set(_id, propertyName, text);

        public bool GetBoolean(string propertyName) 
            => _json.GetBoolean(_id, propertyName);

        public string GetString(string propertyName) 
            => _json.GetString(_id, propertyName);

        internal JsonValue(Json json, int instanceId) {
            _json = json;
            _id = instanceId;
        }
    }
}
