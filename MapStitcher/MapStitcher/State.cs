using ImageMagick;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Linq;
using System.IO;

namespace MapStitcher
{
    public class State
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
        private ConcurrentDictionary<NeedleKey, Point?> needles;

        [JsonRequired]
        private ConcurrentDictionary<string, ConcurrentDictionary<string, Point?>> joins;

        [JsonRequired]
        private HashSet<Tuple<string, NeedleKey>> negativeSearches;

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
            }
        }

        public State()
        {
            ChangeListener = DataflowBlock.NullTarget<State>();
            needles = new ConcurrentDictionary<NeedleKey, Point?>();
            joins = new ConcurrentDictionary<string, ConcurrentDictionary<string, Point?>>();
            sources = new ConcurrentDictionary<string, IMagickImage>();
            negativeSearches = new HashSet<Tuple<string, NeedleKey>>();
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
            lock (lockObject)
            {
                return negativeSearches.Contains(Tuple.Create(haystack, needle)) || JoinsFor(haystack).ContainsKey(needle.Key) || JoinsFor(needle.Key).ContainsKey(haystack);
            }
        }

        public void AddJoinPoint(string haystack, NeedleKey needle, Point? joinPoint, Point needleAnchor)
        {
            // A positive result should be cached as "Connect image1 to image2". Once we have a join, we don't care about the needle/gravity anymore
            // A negative result should be cached as "Couldn't find something with this NeedleKey, but maybe might find something later"
            // For now just going to not cache negative results
            Point? point = null;
            if (joinPoint.HasValue)
            {
                point = new Point(joinPoint.Value.X - needleAnchor.X, joinPoint.Value.Y - needleAnchor.Y);

                Console.WriteLine("jp: {0}, anchor: {1}, offset: {2}", joinPoint, needleAnchor, point);
                lock (lockObject)
                {
                    JoinsFor(haystack).TryAdd(needle.Key, point);
                    JoinsFor(needle.Key).TryAdd(haystack, Inverse(point));
                }
            } else
            {
                lock (lockObject)
                {
                    negativeSearches.Add(Tuple.Create(haystack, needle));
                }
            }


            NotifyChangeListeners();
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

        public Point? GetNeedle(NeedleKey needle)
        {
            Point? result = null;
            if (needles.TryGetValue(needle, out result))
            {
                return result;
            } else
            {
                return null;
            }
        }

        public void ClearJoin(string v1, string v2)
        {
            lock (lockObject)
            {
                JoinsFor(v1).TryRemove(v2, out _);
                JoinsFor(v2).TryRemove(v1, out _);

                negativeSearches.RemoveWhere(x => x.Item1 == v1 && x.Item2.Key == v2);
            }
        }

        public void AddNeedle(NeedleKey needle, Point? v)
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

        private ConcurrentDictionary<string, Point?> JoinsFor(string key)
        {
            return joins.GetOrAdd(key, _ => new ConcurrentDictionary<string, Point?>());
        }
    }
}
