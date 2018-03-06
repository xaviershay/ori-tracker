using System;
using MapStitcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ImageMagick;
using Newtonsoft.Json;

namespace Tests
{
    [TestClass]
    public class SerializationTest
    {
        [TestMethod]
        public void TestValueEquality()
        {
            Assert.AreEqual(
                new NeedleKey { Key = "test.jpg", Gravity = Gravity.North },
                new NeedleKey { Key = "test.jpg", Gravity = Gravity.North }
            );
        }

        [TestMethod]
        public void TestNeedleKey()
        {
            AssertRoundTrip(new NeedleKey { Key = "test.jpg", Gravity = Gravity.North });
            AssertRoundTrip(new NeedleKey { Key = "dir/test.jpg", Gravity = Gravity.South });
            AssertRoundTrip(new NeedleKey { Key = "c:\\dir\\test.jpg", Gravity = Gravity.South });
        }

        private void AssertRoundTrip<T>(T obj)
        {
            var serialized = JsonConvert.SerializeObject(obj);
            var result = JsonConvert.DeserializeObject<T>(serialized);
            Assert.AreEqual(obj, result);
        }
    }
}
