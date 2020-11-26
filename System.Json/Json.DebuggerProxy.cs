using System;

namespace System.Json
{
    partial class Json
    {
        public struct DebuggerRecords
        {
            ReadOnlyMemory<byte> _records;

            public DebuggerRecords(Json records) {
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
}
