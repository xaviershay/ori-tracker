using System;
using System.Windows;
using MapStitcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    [TestClass]
    public class StateTest
    {
        [TestMethod]
        public void TestFetchingInverseJoin()
        {
            var state = new State();

            var haystack = "image-1";
            var needle = new NeedleKey() { Key = "image-2", Gravity = ImageMagick.Gravity.North };

            state.GetOrAddSearch(haystack, needle, () => new State.SearchResult() { HaystackPoint = new Point(5, 5), Distance = 0.1 });

            Assert.AreEqual(new Point(5, 5), state.GetJoin(haystack, needle));
            Assert.AreEqual(new Point(-5, -5), state.GetJoin(needle.Key, new NeedleKey() { Key = "image-1", Gravity = ImageMagick.Gravity.East }));
            Assert.AreEqual(null, state.GetJoin("bogus", needle));
        }
    }
}
