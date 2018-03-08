using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MapStitcher;

namespace Tests
{
    [TestClass]
    public class LinqExtensionTest
    {
        [TestMethod]
        public void OrderFromCenter()
        {
            var numbers = new List<int>()
            {
                1,2,3,4,5
            };

            var expected = new List<int>()
            {
                3,2,4,1,5
            };

            CollectionAssert.AreEqual(expected, numbers.OrderFromCenter().ToList());
        }
    }
}
