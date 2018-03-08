using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapStitcher
{
    public static class LinqExtensions
    {
        public static IEnumerable<T> OrderFromCenter<T>(this IEnumerable<T> source)
        {
            var middle = source.Count() / 2;
            return source
                .Select((x, i) => new KeyValuePair<T, int>(x, i))
                .OrderBy(pair => Math.Abs(middle - pair.Value))
                .Select(pair => pair.Key)
                .ToList();
        }

        public static bool Most<T>(this IEnumerable<T> source, double threshold, Func<T, bool> f)
        {
            double total = 0;
            double success = 0;

            foreach (var x in source)
            {
                if (f.Invoke(x))
                {
                    success++;
                }
                total++;
                // lol stats
                if (total > 50 && success / total < threshold / 2)
                {
                    return false;
                }
            }
            return success / total >= threshold;
        }
    }
}
