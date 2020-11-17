using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace System.Json
{
    public class Json
    {
        List<Record> _records = new List<Record>(); // this will be flattened to a byte buffer
        List<string> _strings = new List<string>(); // this will be flatenned to a byte buffer
        bool _sorted = true;
        int _nextInstanceId = 0;

        private JsonObject Root => new JsonObject(this, 0);

        public void Set(string name, string value) => Root.Set(name, value);
        public void Set(string name, bool value) => Root.Set(name, value);
        public void Set(string name, long value) => Root.Set(name, value);
        public JsonObject SetObject(string name) => Root.SetObject(name);

        public override string ToString()
        {
            var stream = new MemoryStream();
            WriteTo(stream);
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        public void WriteTo(Stream stream)
        {
            if(_records.Count == 0)
            {
                return;
            }
            if (_records.Count == 1)
            {
                WriteLiteral(_records[0], stream);
                return;
            }

            if (!_sorted) _records.Sort();
            _sorted = true;

            var options = new JsonWriterOptions();
            options.Indented = true;
            var writer = new Utf8JsonWriter(stream, options);
            writer.WriteStartObject();

            string previousPropertyName = "";
            int currentInstance = 0;
            int depth = 1;
            for (int i = 0; i < _records.Count; i++)
            {
                var record = _records[i];
                if (record.Name != previousPropertyName)
                {
                    switch (record.Type)
                    {
                        case RecordType.False:
                            CloseObject(writer, currentInstance, record, ref depth);
                            writer.WriteBoolean(record.Name, false);
                            break;
                        case RecordType.True:
                            CloseObject(writer, currentInstance, record, ref depth);
                            writer.WriteBoolean(record.Name, true);
                            break;
                        case RecordType.Int64:
                            CloseObject(writer, currentInstance, record, ref depth);
                            writer.WriteNumber(record.Name, record.Value);
                            break;
                        case RecordType.Null:
                            CloseObject(writer, currentInstance, record, ref depth);
                            writer.WriteNull(record.Name);
                            break;
                        case RecordType.String:
                            CloseObject(writer, currentInstance, record, ref depth);
                            writer.WriteString(record.Name, _strings[(int)record.Value]);
                            break;
                        case RecordType.Clear:
                            CloseObject(writer, currentInstance, record, ref depth);
                            break;
                        case RecordType.Object:
                            CloseObject(writer, currentInstance, record, ref depth);
                            writer.WriteStartObject(record.Name);
                            depth++;
                            break;
                        default: throw new NotImplementedException();
                    }
                }
                previousPropertyName = record.Name;

                if(record.Type == RecordType.Object)
                {
                    currentInstance = record.ObjectProperty;
                }
                else
                {
                    currentInstance = record.Instance;
                }
            }

            while (depth-- > 0)
            {
                writer.WriteEndObject();
            }
            writer.Flush();
            stream.Seek(0, SeekOrigin.Begin);
        }

        private void WriteLiteral(Record record, Stream stream)
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

        private static void CloseObject(Utf8JsonWriter writer, int previousInstance, Record record, ref int depth)
        {
            if (previousInstance != record.Instance)
            {
                writer.WriteEndObject();
                depth--;
            }
        }

        internal void SetCore(int instance, string name, string value)
        {
            _sorted = false;
            _strings.Add(value);
            _records.Add(new Record(instance, _records.Count, name, _strings.Count - 1, RecordType.String, instance));
        }
        internal void SetCore(int instance, string name, bool value)
        {
            _sorted = false;
            _records.Add(new Record(instance, _records.Count, name, 0, value ? RecordType.True : RecordType.False, instance));
        }
        internal void SetCore(int instance, string name, long value)
        {
            _sorted = false;
            _records.Add(new Record(instance, _records.Count, name, value, RecordType.Int64, instance));
        }

        internal JsonObject SetObjectCore(int instance, string name)
        {
            var newObjectId = ++_nextInstanceId;
            _records.Add(new Record(instance, _records.Count, name, newObjectId, RecordType.Object, newObjectId));
            var obj = new JsonObject(this, newObjectId);
            return obj;
        }
    }

    public struct JsonObject
    {
        Json _json;
        int _instance;

        internal JsonObject(Json json, int id)
        {
            _json = json;
            _instance = id;
        }

        public void Set(string name, string value) => _json.SetCore(_instance, name, value);
        public void Set(string name, bool value) => _json.SetCore(_instance, name, value);
        public void Set(string name, long value) => _json.SetCore(_instance, name, value);
        public JsonObject SetObject(string name) => _json.SetObjectCore(_instance, name);
    }

    readonly struct Record : IComparable<Record>
    {
        public readonly int Instance;
        public readonly int ObjectProperty;
        public readonly RecordType Type;
        public readonly int Index;
        public readonly string Name;
        public readonly long Value;

        public Record(int instance, int index, string name, long value, RecordType type, int objectProperty = -1)
        {
            Index = index;
            Name = name;
            Value = value;
            Type = type;
            Instance = instance;
            ObjectProperty = objectProperty;
        }

        private static bool Same(in Record left, in Record right)
        {
            if (left.Instance != right.Instance) return false;
            if (left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public int CompareTo(Record other)
        {
            if (Same(this, other))
            {
                return other.Index.CompareTo(Index);
            }

            var objectProperty = ObjectProperty.CompareTo(other.ObjectProperty);
            if (objectProperty == 0) {
                if (Instance != other.Instance) return Instance.CompareTo(other.Instance);
                var name = StringComparer.InvariantCultureIgnoreCase.Compare(Name, other.Name);
                if(name == 0) return other.Index.CompareTo(Index);
                return name;
            }
            else
            {
                return objectProperty;
            }
        }

        public override string ToString()
            => $"i:{Instance}, o:{ObjectProperty} v:{Value}, x:{Index}, t:{Type}, n:{Name}";
    }

    enum RecordType : int
    {
        String,
        Null,
        False,
        True,
        Int64,
        Clear,
        Object
    }
}
