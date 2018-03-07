using ImageMagick;
using Newtonsoft.Json;
using PersistentObjectCachenet45;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MapStitcher
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public async Task DoNetwork()
        {
            var cacheFile = System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/stitch_cache.json";

            State state = new State();
            try
            {
                state = JsonConvert.DeserializeObject<State>(File.ReadAllText(cacheFile));
                Console.WriteLine($"Using state from {cacheFile}");
            } catch
            {
                Console.WriteLine("Couldn't load cache");
            }
            //state = new State();

            //var workerPool = new LimitedConcurrencyLevelTaskScheduler(Math.Max(Environment.ProcessorCount - 1, 1));
            var workerPool = new LimitedConcurrencyLevelTaskScheduler(1);
            var snapshotState = new ActionBlock<State>((s) =>
            {
                var content = "";
                s.Lock(lockedState => content = JsonConvert.SerializeObject(lockedState));
                File.WriteAllText(cacheFile, content);
            }, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1
            });
            state.ChangeListener = snapshotState;

            var blockOptions = new ExecutionDataflowBlockOptions
            {
                TaskScheduler = workerPool,
                MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded // Handled by underlying scheduler
            };

            var sourceDir = "C:/Users/Xavier/Source/ori-tracker/MapStitcher/Screenshots";

            /*
            MagickImage image1 = new MagickImage($"{sourceDir}/sorrow-1.png");
            MagickImage image2 = new MagickImage($"{sourceDir}/sorrow-2.png");
            */

            var sourceFiles = new List<string>
            {
                $"{sourceDir}/sorrow-1.png",
                $"{sourceDir}/sorrow-2.png",
            };

            var loadFromDiskBlock = new TransformBlock<string, string>(path =>
            {
                state.Image(path);
                return path;
            });

            var cropImagesBlock = new TransformBlock<string, string>(path =>
            {
                // TODO: This is destructive, so that's probably bad in concurrent world?
                var image = state.Image(path);
                var bounds = new MagickGeometry(0, 370, image.Width, image.Height - 250 - 370);
                image.Crop(bounds);
                image.RePage();
                return path;
            }, blockOptions);

            var allGravities = new List<Gravity>()
            {
                Gravity.North,
                Gravity.East,
                Gravity.South,
                Gravity.West
            };

            var gravities = new TransformManyBlock<string, NeedleKey>(path =>
            {
                return allGravities.Select(g => new NeedleKey() { Key = path, Gravity = g });
            }, blockOptions);

            var findNeedleBlock = new TransformBlock<NeedleKey, NeedleKey>(needle =>
            {
                Console.WriteLine("Finding needle: {0}", needle);
                if (!state.NeedleExists(needle))
                {
                    state.AddNeedle(needle, FindHighEntropyStrip(state.Image(needle.Key), needle.Gravity));

                }
                Console.WriteLine("Found needle: {0}", needle);
                return needle;
            }, blockOptions);

            var findJoinBlock = new TransformBlock<Tuple<string, NeedleKey>, string>(t =>
            {
                var haystack = t.Item1;
                var needle = t.Item2;

                if (haystack == needle.Key)
                {
                    Console.WriteLine("Dropping self-join for {0}", haystack);
                    return haystack; // TODO: This doesn't mean anything
                }
                /*
                if (needle.ToString() != "sorrow-2.png|North")
                {
                    Console.WriteLine("Skipping");
                    return haystack;
                }
                */

                if (!state.JoinExists(haystack, needle.Key))
                {
                    Point? potentialAnchor = state.GetNeedle(needle);

                    if (!potentialAnchor.HasValue)
                    {
                        Console.WriteLine("No needle exists for {0}, so no join possible", needle);
                        return null;
                    }
                    var anchor = potentialAnchor.Value;

                    var needleImage = state.Image(needle.Key).Clone();
                    int NeedleSize = 150; // TODO: Move this needle image cropping back into FindNeedle Task
                    needleImage.Crop((int)anchor.X, (int)anchor.Y, NeedleSize, 1);

                    var needleViewImage = state.Image(needle.Key).Clone();
                    needleViewImage.Crop((int)anchor.X, (int)anchor.Y-50, NeedleSize, 100);

                    DisplayImage(Viewer, state.Image(haystack));
                    DisplayImage(Viewer2, needleViewImage);

                    var joinPoint = FindAnchorInImage(needleImage, needle.Gravity, state.Image(haystack));

                    state.AddJoinPoint(haystack, needle.Key, joinPoint, anchor);

                }

                Console.WriteLine("Found join: {0} {1}", System.IO.Path.GetFileName(haystack), System.IO.Path.GetFileName(needle.Key));
                return haystack; // TODO: Figure out best thing to propagate. Maybe when match found?
            }, blockOptions);

            var broadcaster = new BroadcastBlock<string>(null);
            var cartesian = new CartesianProductBlock<string, NeedleKey>();

            var propagate = new DataflowLinkOptions { PropagateCompletion = true };
            var headBlock = loadFromDiskBlock;
            headBlock.LinkTo(cropImagesBlock, propagate);
            cropImagesBlock.LinkTo(broadcaster, propagate);
            broadcaster.LinkTo(gravities, propagate);
            gravities.LinkTo(findNeedleBlock, propagate);

            // Don't propagate completion from left/right sources for cartesian join. It should
            // complete when _both_ are done (which is it's default behaviour)
            broadcaster.LinkTo(cartesian.Left, propagate);
            findNeedleBlock.LinkTo(cartesian.Right, propagate);

            cartesian.LinkTo(findJoinBlock, propagate);

            var sink = new ActionBlock<string>(s => { });
            findJoinBlock.LinkTo(sink, propagate);

            foreach (var file in sourceFiles)
            {
                headBlock.Post(file);
            }
            headBlock.Complete();

            await sink.Completion.ContinueWith(async _ => {
                snapshotState.Complete();
                await snapshotState.Completion;
                Console.WriteLine("Pipeline Finished");
                var joins = state.Joins.GroupBy(k => k.Image1).ToDictionary(k => k.Key, v => v.ToList());
                var seed = joins.First().Key;
                var candidates = new Queue<string>();
                candidates.Enqueue(seed);

                while (candidates.Count > 0)
                {
                    var current = candidates.Dequeue();
                    List<State.Join> localJoins = null;
                    joins.TryGetValue(current, out localJoins);
                    //joins.Remove(seed);
                    // TODO: Can replace with ILookup?

                    if (localJoins != null)
                    {
                        foreach (var localJoin in localJoins)
                        {
                            candidates.Enqueue(localJoin.Image2);
                            Console.WriteLine("Joining {0}", localJoin);
                        }
                    }
                }
                // Put joins into a bag
                // Choose seed image
                // Find all connected images, expand extent ... store offsets somehow?
                // Add all connected images to bag, BFS through them
            });
        }

        private void DisplayImage(Image viewer, IMagickImage image)
        {
            using (var stream = new MemoryStream())
            {
                BitmapImage bitmapImage = new BitmapImage();

                image.Write(stream);

                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();

                // Needed to be able to use object on UI thread
                // https://stackoverflow.com/a/33917169/379639
                bitmapImage.Freeze();

                this.Dispatcher.Invoke(() =>
                {
                    viewer.Source = bitmapImage;
                });
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            Task.Run(() => DoNetwork());
            //DoStuff();
        }
        public async void DoStuff() {
            var sourceDir = "C:/Users/Xavier/Source/ori-tracker/MapStitcher/Screenshots";

            MagickImage image1 = new MagickImage($"{sourceDir}/sorrow-1.png");
            MagickImage image2 = new MagickImage($"{sourceDir}/sorrow-2.png");
            IMagickImage viewerImage1 = image1;
            IMagickImage viewerImage2 = image2;

            var images = new LinkedList<MagickImage>();
            images.AddLast(image1);
            images.AddLast(image2);

            // Crop out header/footer
            foreach (var image in images)
            {
                var bounds = new MagickGeometry(0, 370, image.Width, image.Height - 250 - 370);
                image.Crop(bounds);

                //image.Trim();
                image.RePage();
            }

            /*
            var topStrip = image2.Clone();
            topStrip.Crop(topStrip.Width, 250, Gravity.North);
            topStrip.RePage();

            var bottomStrip = image1.Clone();

            bottomStrip.Crop(bottomStrip.Width, 250, Gravity.South);
            bottomStrip.RePage();

            viewerImage1 = bottomStrip;
            viewerImage2 = topStrip;
            */

            var d = 50;
            Point? possibleAnchor = await cache<Point?>(keyForImage("anchor", image2), () =>
            {
                return FindHighEntropyStrip(image2, Gravity.North);
            });
            //possibleAnchor = new Point(1030, 153);


            var anchor = new Point();
            if (possibleAnchor.HasValue)
            {
                anchor = possibleAnchor.Value;
            } else
            {
                Debug.Fail("Couldn't find an anchor");
            }

            var needle = image2.Clone();
            needle.Crop((int)anchor.X, (int)anchor.Y, d, 1);
            viewerImage1 = image1;
            viewerImage2 = needle;


            Point? potentialJoinPoint = null;


            potentialJoinPoint = await cache(keyForImage("join", image1, needle), () => FindAnchorInImage(needle, Gravity.North, image1));

            //potentialJoinPoint = new Point(923, 1236);
            Console.WriteLine(potentialJoinPoint);
            var joinPoint = potentialJoinPoint.Value;

            var canvas = image1.Clone();
            /*

            MagickReadSettings settings = new MagickReadSettings();
            settings.Width = (int)newWidth;
            settings.Height = (int)newHeight;

            MagickImage canvas = new MagickImage("xc:yellow", settings);
            canvas.Format = MagickFormat.Png32;

            int x1 = (int)anchor.X - (int)joinPoint.X;
            int x2 = 0;

            int y1 = 0;
            int y2 = (int)joinPoint.Y - (int)anchor.Y;
            canvas.Composite(image1, x1, y1, CompositeOperator.Blend);
            canvas.Composite(image2, x2, y2, CompositeOperator.Blend);
            */
            /*
var settings = new MorphologySettings();
settings.Channels = Channels.Alpha;
settings.Method = MorphologyMethod.Distance;
settings.Kernel = Kernel.Euclidean;
settings.KernelArguments = "1,100!";

canvas.Alpha(AlphaOption.Set);
canvas.VirtualPixelMethod = VirtualPixelMethod.Transparent;

canvas.Morphology(settings);
*/
            var newWidth = Math.Max(anchor.X, joinPoint.X) + Math.Max(image2.Width - anchor.X, image1.Width - joinPoint.X);
            var newHeight = Math.Max(anchor.Y, joinPoint.Y) + Math.Max(image2.Height - anchor.Y, image1.Height - joinPoint.Y);
            canvas.Extent((int)newWidth, (int)newHeight);
            canvas.Composite(image2, (int)(joinPoint.X - anchor.X), (int)(joinPoint.Y - anchor.Y));

            viewerImage2 = canvas;

            canvas.Write("C:/Users/Xavier/Documents/test.png");

            /*
            var joinPoint = potentialJoinPoint.Value;

        var joinImage = bottomStrip.Clone();
        joinImage.Crop((int)joinPoint.X, (int)joinPoint.Y, d, 40);
        joinImage.RePage();
            viewerImage1 = joinImage;
            */


            /*
MagickReadSettings settings = new MagickReadSettings();
settings.Width = 4000;
settings.Height = 4000;
            settings.ColorSpace = ColorSpace.sRGB;
MagickImage canvas = new MagickImage("xc:yellow", settings);
canvas.Format = MagickFormat.Png32;
canvas.Composite(image1, 100, 100);
canvas.Composite(image2, 2000, 1000);
            viewerImage1 = canvas;
            */


            // Send image to viewer

            using (var stream = new MemoryStream())
            {
                BitmapImage bitmapImage = new BitmapImage();

                viewerImage1.Write(stream);

                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();

                // Needed to be able to use object on UI thread
                // https://stackoverflow.com/a/33917169/379639
                bitmapImage.Freeze();

                this.Dispatcher.Invoke(() =>
                {
                    Viewer.Source = bitmapImage;
                    this.Title = "DONE";
                });
            }


            using (var stream = new MemoryStream())
            {
                BitmapImage bitmapImage = new BitmapImage();
                viewerImage2.Write(stream);

                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();

                // Needed to be able to use object on UI thread
                // https://stackoverflow.com/a/33917169/379639
                bitmapImage.Freeze();

                this.Dispatcher.Invoke(() =>
                {
                    Viewer2.Source = bitmapImage;
                    this.Title = "DONE";
                });
            }
        }

        private string keyForImage(string v, object image1, object image2 = null)
        {
            var ret = $"{v}-{image1.GetHashCode()}";
            if (image2 != null)
            {
                ret += $"-{image2.GetHashCode()}";
            }
            return ret;
        }

        private async Task<T> cache<T>(string key, Func<T> f)
        {
            var loadedInstance = await PersistentObjectCache.GetObjectAsync<T>(key);
            if (loadedInstance != null && !loadedInstance.Equals(default(T)))
            {
                Console.WriteLine("HIT: " + key);
                return loadedInstance;
            } else
            {
                Console.WriteLine("MISS: " + key);
                var result = f.Invoke();
                await PersistentObjectCache.SetObjectAsync(key, result);
                return result;
            }
        }

        private Point? FindAnchorInImage(IMagickImage needleImage, Gravity needleGravity, IMagickImage haystack)
        {
            // Search in bottom strip for anchor
            var d = 50;
            var pixels = haystack.GetPixels();
            var needle = needleImage.GetPixels().Select(i => i.ToColor()).ToList();
            var searchArea = SearchArea(haystack, Opposite(needleGravity));
            var rows = searchArea.Item1;
            var columns = searchArea.Item2;

            this.Dispatcher.Invoke(() =>
            {
                Progress.Value = 0;
            });

            foreach (var y in rows)
            {
                List<MagickColor> pixelStrip = columns.Select(i => pixels.GetPixel(i, y).ToColor()).ToList();
                foreach (var x in columns)
                {
                    if (x + needle.Count >= haystack.Width)
                    {
                        continue;
                    }
                    var target = Enumerable.Range(x - columns.First(), needle.Count).Select(i => pixelStrip.ElementAt(i));

                    var result = needle.Zip(target, (first, second) => Tuple.Create(first, second)).All(t =>
                    {
                        return t.Item1.FuzzyEquals(t.Item2, new Percentage(15));
                    });

                    /*
                    var resultCandidate = needle.Zip(target, (first, second) => Tuple.Create(first, second)).All(t =>
                    {
                        return t.Item1.FuzzyEquals(t.Item2, new Percentage(15));
                    });
                    */

                    if (result)
                    {
                        var ret = new Point(x, y);
                        var temp = haystack.Clone();
                        temp.Crop(x, y - 50, 100, 100);
                        DisplayImage(Viewer, temp);
                        Console.WriteLine($"FOUND MATCH: ({x}, {y})");
                        return ret;
                    }
                }
                this.Dispatcher.Invoke(() =>
                {
                    Progress.Value = Math.Abs((double)(y - Math.Min(rows.Last(), rows.First())) / (double)(rows.Last() - rows.First()) * 100);
                });
            }
            return null;
        }

        private Gravity Opposite(Gravity gravity)
        {
            switch (gravity)
            {
                case Gravity.North: return Gravity.South;
                case Gravity.South: return Gravity.North;
                case Gravity.East: return Gravity.West;
                case Gravity.West: return Gravity.East;
                default: throw new ArgumentException($"Unhandled gravity: {gravity}");
            }
        }

        public IEnumerable<int> FromTo(int from, int to)
        {
            return Enumerable.Range(from, to - from);
        }

        private Tuple<IEnumerable<int>, IEnumerable<int>> SearchArea(IMagickImage image, Gravity gravity)
        {
            switch (gravity)
            {
                case Gravity.South:
                    return Tuple.Create(
                      FromTo(image.Height - image.Height / 3, image.Height).Reverse(),
                      FromTo(0, image.Width)
                    );
                case Gravity.North:
                    return Tuple.Create(
                        FromTo(0, image.Height / 3),
                        FromTo(0, image.Width)
                    );
                case Gravity.East:
                    return Tuple.Create(
                        FromTo(0, image.Height),
                        FromTo(image.Width - image.Width / 3, image.Width).Reverse()
                    );
                case Gravity.West:
                    return Tuple.Create(
                        FromTo(0, image.Height),
                        FromTo(0, image.Width - image.Width / 3)
                    );
                default:
                    throw new ArgumentException($"Unhandled gravity: {gravity}");
            }
        }

        private Point? FindHighEntropyStrip(IMagickImage image, Gravity gravity)
        {
            var NeedleSize = 150;
            var pixels = image.GetPixels();

            var t1 = DateTime.UtcNow;

            IEnumerable<int> rows = null;
            IEnumerable<int> columns = null;

            Debug.Assert(image.Height > 1 && image.Width > 1, "Assumes non-empty image");
            Debug.Assert(image.Width >= NeedleSize, "Assumes image is at least as big as needle size");

            var searchArea = SearchArea(image, gravity);
            rows = searchArea.Item1;
            columns = searchArea.Item2;

            foreach (var y in rows)
            {
                List<float> pixelStrip = FromTo(columns.Min(), columns.Max()).Select(i => pixels.GetPixel(i, y).ToColor().ToColor().GetBrightness()).ToList();

                foreach (var x in columns)
                {
                    if (x + NeedleSize >= pixelStrip.Count)
                    {
                        continue;
                    }

                    var brightness = Enumerable.Range(x - columns.Min(), NeedleSize).Select(i => pixelStrip.ElementAt(i));

                    var avg = brightness.Average();
                    if (avg > 0.3)
                    {
                        double sum = brightness.Sum(a => Math.Pow(a - avg, 2));
                        var ret = Math.Sqrt((sum) / (brightness.Count() - 1));
                        if (ret > 0.15)
                        {
                            var r = new Point(x, y);
                            Console.WriteLine($"({x}, {y}): {r}");
                            Console.WriteLine(string.Join(",", brightness.ToList()));
                            var t2 = DateTime.UtcNow;
                            Console.WriteLine(t2 - t1);
                            return r;
                        }
                    }
                }
            }
            return null;
        }

        private void Viewer_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition((IInputElement)sender);
            var p2 = Viewer.PointFromScreen(p);
            this.Title = $"{p2.X},{p2.Y}";
        }
    }
}
