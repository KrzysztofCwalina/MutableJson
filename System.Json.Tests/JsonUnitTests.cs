using NUnit.Framework;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.Json;

namespace System.Json.Tests
{
    public class JsonTests
    {
        [Test]
        public void RecordInvariants() {
            var recordShift = Math.Log(Record.SizeInBytes, 2);
            Assert.AreEqual(recordShift, Record.IndexToOffsetShift);

            Assert.AreEqual(sizeof(byte), sizeof(RecordType));
        }

        [Test]
        public void EmptyObject() {
            var json = new Json().Root;
            Assert.AreEqual(RecordType.Object, json.JsonType);
        }

        [Test]
        public void BooleanInlineProperty() {
            var json = new Json().Root;
            json.Set("bar", true);
            Assert.AreEqual(true, json.GetBoolean("bar"));
        }

        [Test]
        public void BooleanStringTableProperty() {
            var property = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

            var json = new Json().Root;
            json.Set(property, true);
            Assert.AreEqual(true, json.GetBoolean(property));
        }

        [Test]
        public void FlatPropertiesOverride() {
            var json = new Json().Root;
            json.Set("foo", 1);
            json.Set("bar", false);
            json.Set("foo", "bar");
            json.Set("bar", true);

            Assert.AreEqual(true, json.GetBoolean("bar"));
            Assert.AreEqual("bar", json.GetString("foo"));
        }

        [Test]
        public void DeserializeSimpleLiterals() {
            byte[] buffer = new byte[256];
            var span = buffer.AsSpan();

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(Record.OffsetType), (int)RecordType.Null);
            var nullJson = Json.Deserialize(buffer).Root;
            Assert.True(nullJson.IsNull);

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(Record.OffsetType), (int)RecordType.True);
            var trueJson = Json.Deserialize(buffer).Root;
            Assert.True(trueJson.ToBoolean());

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(Record.OffsetType), (int)RecordType.False);
            var falseJson = Json.Deserialize(buffer).Root;
            Assert.False(falseJson.ToBoolean());
        }

        [Test]
        public void DeserializeInt64() {
            long number = 12345;

            byte[] buffer = new byte[256];
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(Record.OffsetType), (int)RecordType.Int64);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(Record.OffsetValue), number);

            var json = Json.Deserialize(buffer).Root;
            Assert.True(json.JsonType == RecordType.Int64);
            Assert.AreEqual(number, json.ToInt64());

        }

        //[Test]
        //public void MutationInPlace()
        //{
        //    var json = new Json();
        //    json.Set("bar", false);
        //    json.ToJsonString();
        //    json.Set("bar", true);

        //    Assert.AreEqual(true, json.Root.GetBoolean("bar"));
        //}

        //[Test]
        //public void ObjectProperties()
        //{
        //    var json = new Json();
        //    var addresses = json.SetObject("Addresses");
        //    var name = json.SetObject("Name");
        //    name.Set("First", "John");
        //    name.Set("Last", "Smith");

        //    var home = addresses.SetObject("Home");
        //    var work = addresses.SetObject("Work");

        //    home.Set("Zip", 98052);
        //    home.Set("Country", "US");
        //    work.Set("Zip", 98052);
        //    work.Set("Country", "US");

        //    name.Set("First", "Jim");

        //    var nameObject = json.Root.GetObject("Name");
        //    var firstName = nameObject.GetString("First");
        //    Assert.AreEqual("Jim", firstName);
        //}
        //[Test]
        //public void DeserializeInlineString([Values("", "a", "hello", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] string text) {

        //    byte[] utf8 = Encoding.UTF8.GetBytes(text);

        //    byte[] buffer = new byte[256];
        //    var span = buffer.AsSpan();
        //    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(Record.OffsetType), (int)RecordType.InlineStringLiteral);
        //    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(Record.OffsetStringLength), utf8.Length);
        //    utf8.AsSpan().CopyTo(buffer.AsSpan(Record.OffsetStringStart));

        //    var json = new Json(buffer).Root;
        //    Assert.True(json.JsonType == RecordType.InlineStringLiteral);
        //    Assert.AreEqual(text, json.ToJsonString());
        //}

        //[Test]
        //public void SerializationDemo() {
        //    var json = new Json();
        //    var addresses = json.SetObject("Addresses");
        //    var name = json.SetObject("Name");
        //    name.Set("First", "John");
        //    name.Set("Last", "Smith");

        //    var home = addresses.SetObject("Home");
        //    var work = addresses.SetObject("Work");

        //    home.Set("Zip", 98052);
        //    home.Set("Country", "US");
        //    work.Set("Zip", 98052);
        //    work.Set("Country", "US");

        //    name.Set("First", "Jim");

        //    var stream = new MemoryStream();
        //    json.WriteTo(stream, 'b');
        //    stream.Position = 0;

        //    var deserialized = new Json(stream.ToArray());

        //    var nameObject = deserialized.Root.GetObject("Name");
        //    var firstName = nameObject.GetString("First");
        //    Assert.AreEqual("Jim", firstName);
        //}
    }
}