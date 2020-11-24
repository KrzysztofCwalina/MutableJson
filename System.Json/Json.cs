using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace System.Json
{
    public class Json
    {
        byte[] _data = new byte[1024];
        int _endOfData = 0;

        // TODO: the records are large. they should be accessed by ref
        // database
        Record[] _records = new Record[24]; // this will be flattened to a byte buffer
        const int FirstRecordIndex = 1; // zero is reserved for objects that don't exist
        int _nextFreeSlot = FirstRecordIndex;

        const int RootInstanceId = 1;
        int _nextInstanceId = RootInstanceId;

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
            WriteTo(stream);
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        public void WriteTo(Stream stream) {
            if (RecordCount == 0) {
                return;
            }
            if (RecordCount == 1 && _records[FirstRecordIndex].Name == null) {
                WriteLiteral(ref _records[FirstRecordIndex], stream);
                return;
            }

            var options = new JsonWriterOptions();
            options.Indented = true;
            var writer = new Utf8JsonWriter(stream, options);

            WriteObject(writer, 0);

            writer.Flush();
            stream.Seek(0, SeekOrigin.Begin);
        }

        private int RecordCount => _nextFreeSlot - FirstRecordIndex;
        private void WriteObject(Utf8JsonWriter writer, int index, string name = null) {
            if (name == null) writer.WriteStartObject();
            else writer.WriteStartObject(name);

            var record = _records[index];
            int instance = record.InstanceId;

            while (true) {
                switch (record.Type) {
                    case RecordType.False:
                        writer.WriteBoolean(record.Name, false);
                        break;
                    case RecordType.True:
                        writer.WriteBoolean(record.Name, true);
                        break;
                    case RecordType.Int64:
                        writer.WriteNumber(record.Name, record.Value);
                        break;
                    case RecordType.Null:
                        writer.WriteNull(record.Name);
                        break;
                    case RecordType.String:
                        var span = record.ValueToSpan();
                        writer.WriteString(record.Name, _data.AsSpan(span.index, span.length));
                        break;
                    case RecordType.Clear:
                        break;
                    case RecordType.Object:
                        int objIndex = _records[record.Value].ObjectIndex;
                        WriteObject(writer, objIndex, record.Name);
                        break;
                    default: throw new NotImplementedException();
                }

                if (++index >= _nextFreeSlot) break;
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

        (int, int) AddString(string str) {
            var utf8 = Encoding.UTF8.GetBytes(str);
            var free = _data.AsSpan(_endOfData);
            utf8.AsSpan().CopyTo(free);
            var result = (_endOfData, utf8.Length);
            _endOfData += utf8.Length;
            return result;
        }

        internal void SetCore(int instanceId, string name, string value) {
            var newString = AddString(value);
            var newRecord = new Record(instanceId, name, newString);
            SetRecord(instanceId, name, ref newRecord);
        }

        internal void SetCore(int instanceId, string name, bool value) {
            var newRecord = new Record(instanceId, value ? RecordType.True : RecordType.False, name, 0);
            SetRecord(instanceId, name, ref newRecord);
        }

        internal void SetCore(int instanceId, string name, long value) {
            var newRecord = new Record(instanceId, RecordType.Int64, name, value);
            SetRecord(instanceId, name, ref newRecord);
        }

        internal JsonObject SetObjectCore(int instance, string name) {
            var newObjectId = ++_nextInstanceId;
            var newRecord = new Record(instance, RecordType.Object, name, newObjectId);
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
            int index = _nextFreeSlot;
            _records[_nextFreeSlot] = record;

            // if totally new object
            if (_records[record.InstanceId].ObjectIndex == 0) {
                _records[record.InstanceId].ObjectIndex = _nextFreeSlot;
            }

             _nextFreeSlot++;
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
                if (record.EqualsName(name)) {
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
        public readonly int InstanceId;
        public RecordType Type; // int
        public int NextRecordIndex; // next record of the same instance
        public readonly string Name;
        public long Value;

        public Record(int instance, RecordType type, string name, long value) {
            ObjectIndex = 0;
            InstanceId = instance;
            Type = type;
            NextRecordIndex = -1;
            Name = name;
            Value = value;
        }

        public Record(int instanceId, string name, (int index, int length) stringLocation) : this() {
            InstanceId = instanceId;
            Type = RecordType.String;
            NextRecordIndex = -1;
            Name = name;
            long value = stringLocation.index;
            value = value << 32;
            value |= (long)stringLocation.length;
            Value = value;
        }

        public override string ToString()
            => $"i:{InstanceId}, v:{Value}, t:{Type}, n:{Name}";

        public int CompareName(string name) => StringComparer.OrdinalIgnoreCase.Compare(Name, name);
        public bool EqualsName(string name) => Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        internal (int index, int length) ValueToSpan() {
            int index = (int)(Value >> 32);
            int length = (int)(Value);
            return (index, length);
        }

        public int CompareTo(Record other) {
            return InstanceId.CompareTo(other.InstanceId);
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
