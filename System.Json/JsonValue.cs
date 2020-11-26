using System;

namespace System.Json
{
    public readonly struct JsonValue
    {
        readonly Json _json;
        readonly int _id;

        internal JsonValue(Json json, int instanceId) {
            _json = json;
            _id = instanceId;
        }

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
    }
}
