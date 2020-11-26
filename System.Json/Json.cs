using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace System.Json
{
    public partial class Json
    {
        public Json() { }

        public static Json Deserialize(byte[] bytes) {
            if (bytes.Length < Record.SizeInBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
            var json = new Json(bytes);
            return json;
        }

        public JsonValue Root => new JsonValue(this, instanceId: 0);

        internal RecordType GetRecordType(int recordOffset) {
            if (_records.IsEmpty) return RecordType.Object;
            return (RecordType)BinaryPrimitives.ReadInt32LittleEndian(_records.Span.Slice(recordOffset + Record.OffsetType));
        }

        internal long ToInt64(int instanceId) {
            int recordIndex = FindInstancePropertyOffset(instanceId);
            return GetValue(recordIndex);
        }

        internal string ToJsonString(int instanceId) {
            int recordIndex = FindInstancePropertyOffset(instanceId);
            return GetJsonString(recordIndex);
        }

        internal bool GetBoolean(int instanceId, string propertyName) {
            int recordIndex = FindInstancePropertyOffset(instanceId, propertyName);
            if (recordIndex == -1) throw new KeyNotFoundException();
            var type = GetRecordType(recordIndex);
            if (type == RecordType.False) return false;
            if (type == RecordType.True) return true;
            throw new InvalidCastException();
        }

        internal bool GetInt64(int instanceId, string propertyName) {
            int recordIndex = FindInstancePropertyOffset(instanceId, propertyName);
            if (recordIndex == -1) throw new KeyNotFoundException();
            var type = GetRecordType(recordIndex);
            if (type == RecordType.Int64) GetValue(recordIndex);
            throw new InvalidCastException();
        }

        internal string GetString(int instanceId, string propertyName) {
            int recordIndex = FindInstancePropertyOffset(instanceId, propertyName);
            if (recordIndex == -1) throw new KeyNotFoundException();
            var type = GetRecordType(recordIndex);
            throw new NotImplementedException();
        }

        internal void Set(int instanceId, string propertyName, bool value) {
            var instanceOffset = FindInstanceOffset(instanceId);
            var offset = FindPropertyOffset(instanceOffset, propertyName);
            if (offset == -1) { // new object or property
                var span = AddRecord(out offset);
                var record = new TypeAndNameRecord(span);
                if (!record.TrySetName(propertyName)) {
                    record.Type = value ? RecordType.TruePropertyInline : RecordType.FalsePropertyInline;
                }
                else {
                    throw new NotImplementedException();
                }
                SetObjectOffset(instanceId, offset);
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
}
