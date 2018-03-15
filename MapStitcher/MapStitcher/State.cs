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
        [JsonRequired]
        private Dictionary<NeedleKey, NeedleResult> needles;

        [JsonRequired]
        private Dictionary<SearchKey, SearchResult> searchResults;

        [JsonIgnore]
        private ConcurrentDictionary<string, IMagickImage> sources;

        [JsonIgnore]
        public ITargetBlock<State> ChangeListener;

        [JsonIgnore]
        public IEnumerable<Join> Joins
        {
            get
            {
                return searchResults.Where(x => x.Value.MeetsThreshold()).Select(x => new Join() {
                    Image1 = x.Key.Item1,
                    Image2 = x.Key.Item2.Key,
                    JoinPoint = x.Value.Offset()
                });
            }
        }

        [JsonIgnore]
        private object lockObject = new object();

        public State()
        {
            ChangeListener = DataflowBlock.NullTarget<State>();
            needles = new Dictionary<NeedleKey, NeedleResult>();
            sources = new ConcurrentDictionary<string, IMagickImage>();
            searchResults = new Dictionary<SearchKey, SearchResult>();
        }

        public IMagickImage Image(string key)
        {
            return sources.GetOrAdd(key, (x) => new MagickImage(x));
        }

        public NeedleResult GetOrAddNeedle(NeedleKey needle, Func<NeedleResult> f)
        {
            return GetOrAddCached(needles, needle, f);
        }

        public SearchResult GetOrAddSearch(string haystack, NeedleKey needle, Func<SearchResult> f)
        {
            return GetOrAddCached(searchResults, SearchKey.Create(haystack, needle), f);
        }

        public void ClearNeedle(NeedleKey key)
        {
            needles.Remove(key);
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

        public NeedleResult GetNeedle(NeedleKey needle)
        {
            NeedleResult result = null;
            needles.TryGetValue(needle, out result);
            return result;
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

        private TResult GetOrAddCached<TResult, TKey>(Dictionary<TKey, TResult> cache, TKey key, Func<TResult> f) where TResult : class
        {
            var cached = true;
            TResult result = null;

            cache.TryGetValue(key, out result);

            if (result == null)
            {
                cached = false;
                result = f.Invoke();

                lock (lockObject)
                {
                    cache.Add(key, result);
                }
            }

            if (!cached)
            {
                NotifyChangeListeners();
            }

            return result;
        }

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
    }
}
