using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapStitcher
{
    public static class LinqExtensions
    {

        public static List<int> FromTo(int from, int to)
        {
            return Enumerable.Range(from, to - from).ToList();
        }

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

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>
             (this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> knownKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (knownKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }
    }
}
