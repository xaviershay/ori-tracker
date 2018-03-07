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
        }

        public IMagickImage Image(string key)
        {
            return sources.GetOrAdd(key, (x) => new MagickImage(x));
        }

        public bool JoinExists(string i1, string i2)
        {
            lock (lockObject)
            {
                return JoinsFor(i1).ContainsKey(i2) || JoinsFor(i2).ContainsKey(i1);
            }
        }

        public void AddJoinPoint(string i1, string i2, Point? joinPoint, Point anchor)
        {
            Point? point = null;
            if (joinPoint.HasValue)
            {
                point = new Point(joinPoint.Value.X - anchor.X, joinPoint.Value.Y - anchor.Y);
            }

            lock (lockObject)
            {
                JoinsFor(i1).TryAdd(i2, point);
                JoinsFor(i2).TryAdd(i1, Inverse(point));
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
                // This can probably return null, but keeping exception for now because not expecting it
                throw new ArgumentException($"Needle does not exist in state: {needle}");
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
