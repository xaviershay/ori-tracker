using ImageMagick;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks.Dataflow;
using System.Windows;

namespace MapStitcher
{
    public partial class State
    {

        public struct Join
        {
            public string Image1;
            public string Image2;
            public Point JoinPoint;

            public override string ToString()
            {
                return $"{Path.GetFileName(Image1)}/{Path.GetFileName(Image2)}: {JoinPoint}";
            }
        }

        [JsonRequired]
        private ConcurrentDictionary<NeedleKey, NeedleResult> needles;

        //[JsonRequired]
        //private ConcurrentDictionary<string, ConcurrentDictionary<string, Point?>> joins;

        [JsonRequired]
        private ConcurrentDictionary<SearchKey, SearchResult> searchResults;

        public SearchResult GetOrAddSearch(string haystack, NeedleKey needle, Func<SearchResult> p)
        {
            var cached = true;
            var result = searchResults.GetOrAdd(SearchKey.Create(haystack, needle), (key) =>
            {
                cached = false;
                var point = p.Invoke();
                return point;
            });
            if (!cached)
            {
                NotifyChangeListeners();
            }

            return result;
        }

        [JsonIgnore]
        private ConcurrentDictionary<string, IMagickImage> sources;

        [JsonIgnore]
        private object lockObject = new object();

        [JsonIgnore]
        public ITargetBlock<State> ChangeListener;

        public IEnumerable<Join> Joins
        {
            get
            {
                return searchResults.Where(x => x.Value.MeetsThreshold()).Select(x => new Join() {
                    Image1 = x.Key.Item1,
                    Image2 = x.Key.Item2.Key,
                    JoinPoint = x.Value.Offset()
                });
                /*
                return joins.SelectMany(x => x.Value.SelectMany(y =>
                {
                    if (y.Value.HasValue && x.Key.CompareTo(y.Key) <= 0)
                    {
                        return Enumerable.Repeat(new Join() { Image1 = x.Key, Image2 = y.Key, JoinPoint = y.Value.Value }, 1);
                    } else
                    {
                        return Enumerable.Empty<Join>();
                    }
                }));
                */
            }
        }

        public State()
        {
            ChangeListener = DataflowBlock.NullTarget<State>();
            needles = new ConcurrentDictionary<NeedleKey, NeedleResult>();
            sources = new ConcurrentDictionary<string, IMagickImage>();
            searchResults = new ConcurrentDictionary<SearchKey, SearchResult>();
        }

        public void ClearNeedle(NeedleKey key)
        {
            needles.TryRemove(key, out _);
        }

        public IMagickImage Image(string key)
        {
            return sources.GetOrAdd(key, (x) => new MagickImage(x));
        }

        public bool JoinExists(string haystack, NeedleKey needle)
        {
            return GetJoin(haystack, needle) != null;
        }

        public Point? GetJoin(string haystack, NeedleKey needle)
        {
            var key = Tuple.Create(haystack, needle);
            lock(lockObject)
            {
                // This isn't very efficient, but going for correctness first
                var blah = searchResults.FirstOrDefault(x => ((x.Key.Item1 == haystack && x.Key.Item2.Key == needle.Key) || (x.Key.Item1 == needle.Key && x.Key.Item2.Key == haystack)));

                if (blah.Key == null)
                {
                    return null;
                } else if (blah.Key.Item1 == haystack)
                {
                    return blah.Value.HaystackPoint;
                } else if (blah.Key.Item1 == needle.Key)
                {
                    return Inverse(blah.Value.HaystackPoint);
                } else
                {
                    // Shouldn't happen
                    return null;
                }
            }
        }

        internal void ClearJoins()
        {
            this.searchResults.Clear();
        }

        public void Lock(Action<State> f)
        {
            lock (lockObject)
            {
                f.Invoke(this);
            }
        }

        public bool NeedleExists(NeedleKey needle)
        {
            lock (lockObject)
            {
                return needles.ContainsKey(needle);
            }
        }

        public NeedleResult GetNeedle(NeedleKey needle)
        {
            NeedleResult result = null;
            needles.TryGetValue(needle, out result);
            return result;
        }

        public void ClearJoin(string v1, string v2)
        {
            throw new ArgumentException("Unimplemented");
            lock (lockObject)
            {
                /*
                JoinsFor(v1).TryRemove(v2, out _);
                JoinsFor(v2).TryRemove(v1, out _);
                */

                //searchResults //RemoveWhere(x => x.Item1 == v1 && x.Item2.Key == v2);
            }
        }

        public void AddNeedle(NeedleKey needle, NeedleResult v)
        {
            lock (lockObject)
            {
                needles.TryAdd(needle, v);
            }

            NotifyChangeListeners();
        }

        private void NotifyChangeListeners()
        {
            ChangeListener.Post(this);
        }

        private Point? Inverse(Point? point)
        {
            if (point.HasValue)
            {
                return new Point(-point.Value.X, -point.Value.Y);
            } else
            {
                return null;
            }
        }

        /*
        private ConcurrentDictionary<string, Point?> JoinsFor(string key)
        {
            return joins.GetOrAdd(key, _ => new ConcurrentDictionary<string, Point?>());
        }
        */
    }
}
