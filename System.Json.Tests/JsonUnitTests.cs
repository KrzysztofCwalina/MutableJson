using NUnit.Framework;
using System.IO;
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
            Assert.AreEqual(true, json.Root.GetBoolean(null));
        }

        [Test]
        public void FlatProperties()
        {
            var json = new Json();
            json.Set("foo", 1);
            json.Set("bar", false);
            json.Set("foo", "bar");
            json.Set("bar", true);

            Assert.AreEqual(true, json.Root.GetBoolean("bar"));
            Assert.AreEqual("bar", json.Root.GetString("foo"));
        }

        [Test]
        public void MutationInPlace()
        {
            var json = new Json();
            json.Set("bar", false);
            json.ToJsonString();
            json.Set("bar", true);

            Assert.AreEqual(true, json.Root.GetBoolean("bar"));
        }

        [Test]
        public void ObjectProperties()
        {
            var json = new Json();
            var addresses = json.SetObject("Addresses");
            var name = json.SetObject("Name");
            name.Set("First", "John");
            name.Set("Last", "Smith");

            var home = addresses.SetObject("Home");
            var work = addresses.SetObject("Work");

            home.Set("Zip", 98052);
            home.Set("Country", "US");
            work.Set("Zip", 98052);
            work.Set("Country", "US");

            name.Set("First", "Jim");

            var nameObject = json.Root.GetObject("Name");
            var firstName = nameObject.GetString("First");
            Assert.AreEqual("Jim", firstName);
        }

        [Test]
        public void Serialization() {
            var json = new Json();
            var addresses = json.SetObject("Addresses");
            var name = json.SetObject("Name");
            name.Set("First", "John");
            name.Set("Last", "Smith");

            var home = addresses.SetObject("Home");
            var work = addresses.SetObject("Work");

            home.Set("Zip", 98052);
            home.Set("Country", "US");
            work.Set("Zip", 98052);
            work.Set("Country", "US");

            name.Set("First", "Jim");

            var stream = new MemoryStream();
            json.WriteTo(stream, 'b');
            stream.Position = 0;

            var deserialized = new Json(stream.ToArray());

            var nameObject = deserialized.Root.GetObject("Name");
            var firstName = nameObject.GetString("First");
            Assert.AreEqual("Jim", firstName);
        }
    }
}