using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// TODO: the records are large. they should be accessed by ref

namespace System.Json
{
    public class Json
    {
        const int FirstRecordIndex = 1; // O is reserved for database header
        const int RootInstanceId = 1;

        // database
        Record[] _records = new Record[24]; // this can be flattened to a byte buffer
        byte[] _data = new byte[1024];

        public Json() {
            // TODO: don't access these directly. Have APIs for it. Same elsewhere in the file
            _records[0].Value = FirstRecordIndex; // next free record index
            _records[0].InstanceId = RootInstanceId; // next instance ID
            _records[0].ObjectIndex = 0; // length of data
        }

        public Json(byte[] serialized) {
            var first = MemoryMarshal.Read<Record>(serialized);
            var recordsLength = (int)first.Value;
            var dataLength = first.ObjectIndex;
            _records = new Record[recordsLength];
            var recordBytes = MemoryMarshal.AsBytes(_records.AsSpan());
            serialized.AsSpan(0, recordBytes.Length).CopyTo(recordBytes);
            _data = serialized.AsSpan(recordBytes.Length, DataLength).ToArray();
        }

        int NextFreeRecordIndex => (int)_records[0].Value;
        int NextInstanceId => _records[0].InstanceId;
        int DataLength => _records[0].ObjectIndex;

        public JsonObject Root => new JsonObject(this, RootInstanceId);

        #region getters
        internal bool GetBoolean(int instanceId, string name) {
            var index = FindRecord(instanceId, name);
            var record = _records[index];

            if (record.Type == RecordType.False) return false;
            if (record.Type == RecordType.True) return true;
            else throw new InvalidCastException();
        }

        internal Span<byte> GetString(int instanceId, string name) {
            var index = FindRecord(instanceId, name);
            var record = _records[index];

            var span = record.ValueToSpan();
            if (record.Type == RecordType.String) return _data.AsSpan(span.index, span.length);
            else throw new InvalidCastException();
        }

        internal JsonObject GetObject(int instanceId, string name) {
            var index = FindRecord(instanceId, name);
            var record = _records[index];
            if (record.Type == RecordType.Object) return new JsonObject(this, (int)record.Value);
            else throw new InvalidCastException();
        }
        #endregion

        #region setters
        public void Set(string name, string value) => Root.Set(name, value);
        public void Set(string name, bool value) => Root.Set(name, value);
        public void Set(string name, long value) => Root.Set(name, value);
        public JsonObject SetObject(string name) => Root.SetObject(name);
        #endregion

        public string ToJsonString() {
            var stream = new MemoryStream();
            WriteTo(stream, 'u');
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="format">Use 'u' for UTF8. Use 'b' for binary</param>
        public void WriteTo(Stream stream, StandardFormat format) {
            if (format.Symbol == 'u') {
                if (RecordCount == 0) {
                    return;
                }
                if (RecordCount == 1 && _records[FirstRecordIndex].Name.IsEmpty) {
                    WriteLiteral(ref _records[FirstRecordIndex], stream);
                    return;
                }

                var options = new JsonWriterOptions();
                options.Indented = true;
                var writer = new Utf8JsonWriter(stream, options);

                WriteObject(writer, 0, ReadOnlySpan<byte>.Empty);
                writer.Flush();
            }
            else if (format.Symbol == 'b') {
                var recordsToWrite = _records.AsSpan(0, NextFreeRecordIndex);
                Span<byte> rcordBytes = MemoryMarshal.AsBytes(recordsToWrite);
                stream.Write(rcordBytes.ToArray(), 0, rcordBytes.Length); // TODO: this should be optimized
                stream.Write(_data, 0, DataLength);
                stream.Flush();
            }
            else throw new ArgumentOutOfRangeException(nameof(format));
        }

        private int RecordCount => NextFreeRecordIndex - FirstRecordIndex;
        private void WriteObject(Utf8JsonWriter writer, int index, ReadOnlySpan<byte> name) {
            if (name.IsEmpty) writer.WriteStartObject();
            else writer.WriteStartObject(name);

            var record = _records[index];
            int instance = record.InstanceId;

            while (true) {
                switch (record.Type) {
                    case RecordType.False:
                        writer.WriteBoolean(record.Name.ToUtf8(_data), false);
                        break;
                    case RecordType.True:
                        writer.WriteBoolean(record.Name.ToUtf8(_data), true);
                        break;
                    case RecordType.Int64:
                        writer.WriteNumber(record.Name.ToUtf8(_data), record.Value);
                        break;
                    case RecordType.Null:
                        writer.WriteNull(record.Name.ToUtf8(_data));
                        break;
                    case RecordType.String:
                        var span = record.ValueToSpan();
                        writer.WriteString(record.Name.ToUtf8(_data), _data.AsSpan(span.index, span.length));
                        break;
                    case RecordType.Clear:
                        break;
                    case RecordType.Object:
                        int objIndex = _records[record.Value].ObjectIndex;
                        WriteObject(writer, objIndex, record.Name.ToUtf8(_data));
                        break;
                    default: throw new NotImplementedException();
                }

                if (++index >= NextFreeRecordIndex) break;
                record = _records[index];
                if (instance != record.InstanceId) break;
            }
            writer.WriteEndObject();
        }

        private void WriteLiteral(ref Record record, Stream stream) {
            byte[] payload;
            switch (record.Type) {
                case RecordType.Null:
                    payload = Encoding.UTF8.GetBytes("null");
                    break;
                case RecordType.False:
                    payload = Encoding.UTF8.GetBytes("false");
                    break;
                case RecordType.True:
                    payload = Encoding.UTF8.GetBytes("true");
                    break;
                default:
                    throw new NotImplementedException();
            }
            stream.Write(payload, 0, payload.Length);
        }

        Text AddString(string str) {
            if (str == null) return default;

            var utf8 = Encoding.UTF8.GetBytes(str);
            var free = _data.AsSpan(DataLength);
            utf8.AsSpan().CopyTo(free);
            var result = new Text(DataLength, utf8.Length);
            _records[0].ObjectIndex = DataLength + utf8.Length;
            return result;
        }

        internal void SetCore(int instanceId, string name, string value) {
            var newString = AddString(value);
            var newName = AddString(name); // TODO (pri 0): only add name if adding a new record. Updates don't need to add name.
            var newRecord = new Record(instanceId, newName, newString);
            SetRecord(instanceId, name, ref newRecord);
        }

        internal void SetCore(int instanceId, string name, bool value) {
            var newName = AddString(name);
            var newRecord = new Record(instanceId, newName, value ? RecordType.True : RecordType.False, 0);
            SetRecord(instanceId, name, ref newRecord);
        }

        internal void SetCore(int instanceId, string name, long value) {
            var newName = AddString(name);
            var newRecord = new Record(instanceId, newName, RecordType.Int64, value);
            SetRecord(instanceId, name, ref newRecord);
        }

        internal JsonObject SetObjectCore(int instance, string name) {
            var newObjectId = NextInstanceId;
            _records[0].InstanceId = newObjectId + 1;
            var newName = AddString(name);
            var newRecord = new Record(instance, newName, RecordType.Object, newObjectId);
            SetRecord(instance, name, ref newRecord);
            var obj = new JsonObject(this, newObjectId);
            return obj;
        }

        void SetRecord(int instanceId, string name, ref Record newRecord) {
            if (TryFindRecord(instanceId, name, out int index)) {
                var old = _records[index];
                if(old.Type == RecordType.Object) {
                    throw new NotImplementedException(); // TODO: implement
                }
                newRecord.ObjectIndex = old.ObjectIndex;
                newRecord.NextRecordIndex = old.NextRecordIndex;
                _records[index] = newRecord;
            }
            else {
                int newIndex = AddRecord(ref newRecord);
                if (index != -1) {
                    _records[index].NextRecordIndex = newIndex;
                }
            }
        }

        /// <returns>index of the added record</returns>
        int AddRecord(ref Record record) {
            int index = NextFreeRecordIndex;
            _records[NextFreeRecordIndex] = record;

            // if totally new object
            if (_records[record.InstanceId].ObjectIndex == 0) {
                _records[record.InstanceId].ObjectIndex = NextFreeRecordIndex;
            }

            _records[0].Value = NextFreeRecordIndex + 1;
            return index;
        }

        /// <param name="recordIndex">found index, or last record's index if name not found, or -1 if object does not exist</param>
        bool TryFindRecord(int instanceId, string name, out int recordIndex) {

            recordIndex = _records[instanceId].ObjectIndex;
            if(recordIndex == 0) {
                recordIndex = -1;
                return false;
            }

            Debug.Assert(_records[recordIndex].InstanceId == instanceId);

            int index = recordIndex;
            while (index >= 0) {
                var record = _records[index];
                Debug.Assert(record.InstanceId == instanceId);
                recordIndex = index;
                if (record.EqualsName(_data, name)) {
                    return true;
                }
                else {
                    index = record.NextRecordIndex;
                }
            }
            return false;       
        }
        int FindRecord(int instanceId, string name) {
            if (TryFindRecord(instanceId, name, out int index)) {
                return index;
            }
            throw new KeyNotFoundException();
        }
    }

    public readonly struct JsonObject
    {
        readonly Json _json;
        readonly int _id;

        internal JsonObject(Json json, int instanceId) {
            _json = json;
            _id = instanceId;
        }

        public void Set(string name, string value) => _json.SetCore(_id, name, value);
        public void Set(string name, bool value) => _json.SetCore(_id, name, value);
        public void Set(string name, long value) => _json.SetCore(_id, name, value);
        public JsonObject SetObject(string name) => _json.SetObjectCore(_id, name);

        public JsonObject GetObject(string name) => _json.GetObject(_id, name);

        public bool GetBoolean(string name) => _json.GetBoolean(_id, name);
        public Span<byte> GetUtf8(string name) => _json.GetString(_id, name);

        public string GetString(string name) => Encoding.UTF8.GetString(_json.GetString(_id, name).ToArray());
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Record : IComparable<Record>
    {
        public int ObjectIndex; // if this record is at index X, object with instance ID X starts at ObjectIndex
        public int InstanceId;
        public RecordType Type; // int
        public int NextRecordIndex; // next record of the same instance
        public Text Name;
        public long Value;

        public Record(int instanceId, Text propertyName, RecordType propertyType, long propertyValue) {
            ObjectIndex = 0;
            InstanceId = instanceId;
            Type = propertyType;
            NextRecordIndex = -1;
            Name = propertyName;
            Value = propertyValue;
        }

        public Record(int instanceId, Text propertyName, Text propertyText) {
            ObjectIndex = 0;
            InstanceId = instanceId;
            Type = RecordType.String;
            NextRecordIndex = -1;
            Name = propertyName;
            Value = propertyText.AsLong();
        }

        public override string ToString()
            => $"i:{InstanceId}, v:{Value}, t:{Type}, n:{Name}";

        public bool EqualsName(ReadOnlySpan<byte> table, string name) => Name.Equals(table, name);

        internal (int index, int length) ValueToSpan() {
            int index = (int)(Value >> 32);
            int length = (int)(Value);
            return (index, length);
        }

        public int CompareTo(Record other) {
            return InstanceId.CompareTo(other.InstanceId);
        }
    }

    readonly struct Text
    {
        readonly int _index;
        readonly int _length;

        public Text(int index, int length) {
            _index = index;
            _length = length;
        }

        public bool IsEmpty => _length == 0;
        
        public bool Equals(ReadOnlySpan<byte> table, string other) {
            if (_length == 0 && other == null) return true;
            if (_length != other.Length) return false; // TODO: this does not support unicode.
            // TODO: this needs to be optimized
            var utf8 = ToUtf8(table);
            var otherUtf8 = Encoding.UTF8.GetBytes(other);
            return utf8.SequenceEqual(otherUtf8);
        }

        public ReadOnlySpan<byte> ToUtf8(ReadOnlySpan<byte> table)
            => table.Slice(_index, _length);   
        
        public long AsLong() {
            long value = _index;
            value = value << 32;
            value |= (long)_length;
            return value;
        }
    }

    enum RecordType : int
    {
        Clear = 0,
        String,
        Null,
        False,
        True,
        Int64,
        Object
    }
}
