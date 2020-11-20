using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace System.Json
{
    public class Json
    {
        byte[] _data = new byte[1024];
        int _endOfData = 0;

        // database
        int[] _objectLocations = new int[24]; // this should be merged into records
        Record[] _records = new Record[24]; // this will be flattened to a byte buffer
        int _recordCount = 0;

        bool _sorted = true;
        int _nextInstanceId = 0;

        public JsonObject Root => new JsonObject(this, 0);

        public void Set(string name, string value) => Root.Set(name, value);
        public void Set(string name, bool value) => Root.Set(name, value);
        public void Set(string name, long value) => Root.Set(name, value);
        public JsonObject SetObject(string name) => Root.SetObject(name);

        public string ToJsonString()
        {
            var stream = new MemoryStream();
            WriteTo(stream);
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        public void WriteTo(Stream stream)
        {
            if (_recordCount == 0)
            {
                return;
            }
            if (_recordCount == 1 && _records[0].Name == null)
            {
                WriteLiteral(ref _records[0], stream);
                return;
            }

            Sort();

            var options = new JsonWriterOptions();
            options.Indented = true;
            var writer = new Utf8JsonWriter(stream, options);

            WriteObject(writer, 0, _objectLocations);
            
            writer.Flush();
            stream.Seek(0, SeekOrigin.Begin);
        }

        private void WriteObject(Utf8JsonWriter writer, int index, int[] locations, string name = null) 
        {
            if (name == null) writer.WriteStartObject();
            else writer.WriteStartObject(name);

            var record = _records[index];
            int instance = record.InstanceId;

            while (true)
            {
                switch (record.Type)
                {
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
                        int objIndex = locations[record.Value];
                        WriteObject(writer, objIndex, locations, record.Name);
                        break;
                    default: throw new NotImplementedException();
                }
           
                if (++index >= _recordCount) break; 
                record = _records[index];
                if (instance != record.InstanceId) break;
            } 
            writer.WriteEndObject();
        }

        class DuplicateComparer : Comparer<Record>
        {
            public new static readonly DuplicateComparer Default = new DuplicateComparer();

            public override int Compare(Record left, Record right)
            {
                var instance= left.InstanceId.CompareTo(right.InstanceId);
                if (instance != 0) return instance;

                var name = left.CompareName(right.Name);
                if (name == 0) return right.Index.CompareTo(left.Index);
                return name;
            }
        }
        void Sort()
        {
            if (!_sorted)
            {
                Array.Sort(_records, 0, _recordCount, DuplicateComparer.Default);

                // remove duplicates
                int firstIndex = 0;
                var first = _records[0];
                for (int secondIndex=1; secondIndex<_recordCount; secondIndex++)
                {
                    var second = _records[secondIndex];
                    // if different properties
                    if(first.InstanceId != second.InstanceId || !first.Name.Equals(second.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        if(secondIndex-firstIndex>1)
                        {
                            _records[firstIndex + 1] = second;
#if DEBUG
                            _records[secondIndex] = default;
#endif
                        }
                        firstIndex++;
                        first = second;
                    }
                    else
                    {
#if DEBUG
                        _records[secondIndex] = default;
#endif
                    }
                }
                _recordCount = firstIndex + 1;

                for (int i = 0; i < _recordCount; i++)
                {
                    var record = _records[i];
                    if (_objectLocations[record.InstanceId] == 0 && record.InstanceId != 0) _objectLocations[record.InstanceId] = i;
                }
            }
            _sorted = true;
        }

        void AddRecord(ref Record record)
        {
            _records[_recordCount] = record;
            _recordCount++;
            if (_recordCount == 1) return;
            if (_records[_recordCount - 2].InstanceId == record.InstanceId) return;

            if(_objectLocations[record.InstanceId] == 0)
            {
                _objectLocations[record.InstanceId] = _recordCount - 1;
                return;
            }
            _sorted = false; 
        }

        private void WriteLiteral(ref Record record, Stream stream)
        {
            byte[] payload;
            switch (record.Type)
            {
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

        (int, int) AddString(string str)
        {
            var utf8 = Encoding.UTF8.GetBytes(str);
            var free = _data.AsSpan(_endOfData);
            utf8.AsSpan().CopyTo(free);
            var result = (_endOfData, utf8.Length);
            _endOfData += utf8.Length;
            return result;
        }

        internal void SetCore(int instanceId, string name, string value)
        {
            var location = AddString(value);
            if (TryFindRecord(instanceId, name, out int index))
            {
                _records[index] = new Record(instanceId, index, name, location);
                return;
            }
            var newRecord = new Record(instanceId, _recordCount, name, location);
            AddRecord(ref newRecord);
        }

        internal void SetCore(int instanceId, string name, bool value)
        {
            if (TryFindRecord(instanceId, name, out int index)) {
                _records[index] = new Record(instanceId, index, name, 0, value ? RecordType.True : RecordType.False);
                return;
            }
            var newRecord = new Record(instanceId, _recordCount, name, 0, value ? RecordType.True : RecordType.False);
            AddRecord(ref newRecord);
        }

        internal void SetCore(int instanceId, string name, long value)
        {
            if (TryFindRecord(instanceId, name, out int index))  {
                _records[index] = new Record(instanceId, index, name, value, RecordType.Int64);
                return;
            }
            var newRecord = new Record(instanceId, _recordCount, name, value, RecordType.Int64);
            AddRecord(ref newRecord);
        }

        internal JsonObject SetObjectCore(int instance, string name)
        {
            var newObjectId = ++_nextInstanceId;
            var record = new Record(instance, _recordCount, name, newObjectId, RecordType.Object);
            AddRecord(ref record);
            var obj = new JsonObject(this, newObjectId);
            return obj;
        }

        internal bool TryFindRecord(int instanceId, string name, out int recordIndex)
        {
            if (_sorted)
            {
                var objectStart = _objectLocations[instanceId];
                for (int index = objectStart; index < _recordCount; index++)
                {
                    var record = _records[index];
                    if (record.InstanceId != instanceId) break;
                    if (record.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        recordIndex = index;
                        return true;
                    }
                }
            }
            recordIndex = -1;
            return false;
        }

        internal bool GetBoolean(int instanceId, string name)
        {
            if(!TryFindRecord(instanceId, name, out int index)) {
                if (_sorted) throw new KeyNotFoundException();
                else throw new NotImplementedException();
            }

            var record = _records[index];
     
            if (record.Type == RecordType.False) return false;
            if (record.Type == RecordType.True) return true;
            else throw new InvalidCastException();
        }
    }

    public readonly struct JsonObject
    {
        readonly Json _json;
        readonly int _id;

        internal JsonObject(Json json, int instanceId)
        {
            _json = json;
            _id = instanceId;
        }

        public void Set(string name, string value) => _json.SetCore(_id, name, value);
        public void Set(string name, bool value) => _json.SetCore(_id, name, value);
        public void Set(string name, long value) => _json.SetCore(_id, name, value);
        public JsonObject SetObject(string name) => _json.SetObjectCore(_id, name);

        public bool GetBoolean(string name) => _json.GetBoolean(_id, name);
    }

    readonly struct Record
    {
        public readonly int InstanceId;
        public readonly RecordType Type;
        public readonly int Index; // This should be removed. Before sort, duplicate records should be erased
        public readonly string Name;
        public readonly long Value;

        public Record(int instance, int index, string name, long value, RecordType type)
        {
            Index = index;
            Name = name;
            Value = value;
            Type = type;
            InstanceId = instance;
        }

        public Record(int instanceId, int index, string name, (int index, int length) stringLocation) : this()
        {
            Type = RecordType.String;
            InstanceId = instanceId;
            Index = index;
            Name = name;

            long value = stringLocation.index;
            value = value << 32;
            value |= (long)stringLocation.length;
            Value = value;
        }

        public override string ToString()
            => $"i:{InstanceId}, v:{Value}, t:{Type}, n:{Name}";

        public int CompareName(string name) => StringComparer.OrdinalIgnoreCase.Compare(Name, name);

        internal (int index, int length) ValueToSpan()
        {
            int index = (int)(Value >> 32);
            int length = (int)(Value);
            return (index, length);
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
