using NUnit.Framework;
using System.Text.Json;

namespace System.Json.Tests
{
    public class JsonTests
    {
        [Test]
        public void Literal()
        {
            var json = new Json();
            json.Set(null, true);
            var parsed = JsonDocument.Parse(json.ToString()).RootElement;
            Assert.AreEqual(true, parsed.GetBoolean());
        }

        [Test]
        public void FlatProperties()
        {
            var json = new Json();
            json.Set("foo", 1);
            json.Set("bar", false);
            json.Set("foo", "bar");
            json.Set("bar", true);

            var parsed = JsonDocument.Parse(json.ToString()).RootElement;
            Assert.AreEqual(true, parsed.GetProperty("bar").GetBoolean());
            Assert.AreEqual("bar", parsed.GetProperty("foo").GetString());
        }

        [Test]
        public void ObjectProperties()
        {
            var json = new Json();
            var address = json.SetObject("Address");
            var name = json.SetObject("Name");
            name.Set("First", "John");
            name.Set("Last", "Smith");
            address.Set("Zip", 98052);
            address.Set("Country", "US");
            name.Set("First", "Jim");

            var jsonText = json.ToString();
            var parsed = JsonDocument.Parse(jsonText).RootElement;
            Assert.AreEqual("Jim", parsed.GetProperty("Name").GetProperty("First").GetString());
        }
    }
}