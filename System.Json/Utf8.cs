using System.Text;

namespace System.Json
{
    class Utf8
    {
        public static bool TryToUtf8(ref Span<byte> buffer, string source, out int written) {
            byte[] utf8 = Encoding.UTF8.GetBytes(source); // TODO: this needs to be optimized
            utf8.AsSpan().TryCopyTo(buffer);
            written = utf8.Length;
            return true;
        }
    }
}
