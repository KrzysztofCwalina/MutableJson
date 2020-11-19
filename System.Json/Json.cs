using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace System.Json
{
    public class Json
    {
        Record[] _records = new Record[24]; // this will be flattened to a byte buffer
        int _recordCount = 0;

        List<string> _strings = new List<string>(); // this will be flatenned to a byte buffer
        bool _sorted = true;
        int _nextInstanceId = 0;

        private JsonObject Root => new JsonObject(this, 0, 0);

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
            if (_recordCount == 1)
            {
                WriteLiteral(ref _records[0], stream);
                return;
            }

            Sort();

            var objectLocations = new int[100]; // TODO: this should not be hard coded
            for(int i=0; i<_recordCount; i++)
            {
                var record = _records[i];
                if (objectLocations[record.Instance] == 0 && record.Instance != 0) objectLocations[record.Instance] = i;
            }

            var options = new JsonWriterOptions();
            options.Indented = true;
            var writer = new Utf8JsonWriter(stream, options);

            WriteObject(writer, 0, objectLocations);
            
            writer.Flush();
            stream.Seek(0, SeekOrigin.Begin);
        }

        private void WriteObject(Utf8JsonWriter writer, int index, int[] locations, string name = null) 
        {
            if (name == null) writer.WriteStartObject();
            else writer.WriteStartObject(name);

            var record = _records[index];
            int instance = record.Instance;

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
                        writer.WriteString(record.Name, _strings[(int)record.Value]);
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
                if (instance != record.Instance) break;
            } 
            writer.WriteEndObject();
        }

        class DuplicateComparer : Comparer<Record>
        {
            public new static readonly DuplicateComparer Default = new DuplicateComparer();

            public override int Compare(Record left, Record right)
            {
                //var ic = left.Index.CompareTo(right.Index);
                var instance= left.Instance.CompareTo(right.Instance);
                if (instance != 0) return instance;

                var name = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
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
                    if(first.Instance != second.Instance || !first.Name.Equals(second.Name, StringComparison.OrdinalIgnoreCase))
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
            }
            _sorted = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddRecord(ref Record record)
        {
            _records[_recordCount] = record;
            _recordCount++;
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

        private static void CloseObject(Utf8JsonWriter writer, int previousInstance, ref Record record, ref int depth)
        {
            if (previousInstance != record.Instance)
            {
                writer.WriteEndObject();
                depth--;
            }
        }

        internal void SetCore(int instance, string name, string value, int depth)
        {
            _strings.Add(value);
            var record = new Record(instance, _recordCount, name, _strings.Count - 1, RecordType.String, depth);
            AddRecord(ref record);
        }
        internal void SetCore(int instance, string name, bool value, int depth)
        {
            var record = new Record(instance, _recordCount, name, 0, value ? RecordType.True : RecordType.False, depth);
            AddRecord(ref record);
        }
        internal void SetCore(int instance, string name, long value, int depth)
        {
            var record = new Record(instance, _recordCount, name, value, RecordType.Int64, depth);
            AddRecord(ref record);
        }

        internal JsonObject SetObjectCore(int instance, string name, int depth)
        {
            var newObjectId = ++_nextInstanceId;
            var record = new Record(instance, _recordCount, name, newObjectId, RecordType.Object, depth);
            AddRecord(ref record);
            var obj = new JsonObject(this, newObjectId, depth+1);
            return obj;
        }
    }

    public struct JsonObject
    {
        Json _json;
        int _instance;
        int _depth;

        internal JsonObject(Json json, int id, int depth)
        {
            _json = json;
            _instance = id;
            _depth = depth;
        }

        public void Set(string name, string value) => _json.SetCore(_instance, name, value, _depth);
        public void Set(string name, bool value) => _json.SetCore(_instance, name, value, _depth);
        public void Set(string name, long value) => _json.SetCore(_instance, name, value, _depth);
        public JsonObject SetObject(string name) => _json.SetObjectCore(_instance, name, _depth);
    }

    readonly struct Record
    {
        public readonly int Instance;
        public readonly int Depth;
        public readonly RecordType Type;
        public readonly int Index;
        public readonly string Name;
        public readonly long Value;

        public Record(int instance, int index, string name, long value, RecordType type, int depth)
        {
            Index = index;
            Name = name;
            Value = value;
            Type = type;
            Instance = instance;
            Depth = depth;
        }

        public int ObjectProperty
        {
            get
            {
                if (Type == RecordType.Object) return (int)Value;
                return Instance;
            }
        }

        public override string ToString()
            => $"i:{Instance}, d:{Depth} v:{Value}, t:{Type}, n:{Name}";
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
