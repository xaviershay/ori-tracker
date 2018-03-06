using ImageMagick;
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
    public class JoinData
    {
        public string Source;
        public string Target;
        public Point? Join; // If null, no join was found between the two
    }

    public class NeedleKey
    {
        public string Key;
        public Gravity Gravity;
    }

    public class State
    {
        public ConcurrentDictionary<string, IMagickImage> sources = new ConcurrentDictionary<string, IMagickImage>();
        public ConcurrentDictionary<NeedleKey, Point?> needles = new ConcurrentDictionary<NeedleKey, Point?>();
        public ConcurrentDictionary<string, JoinData> joins = new ConcurrentDictionary<string, JoinData>();

        public IMagickImage Image(string key)
        {
            return sources.GetOrAdd(key, (x) => new MagickImage(x));
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public async Task DoNetwork()
        {
            var state = await cache("state2", () => new State());
            //var workerPool = new LimitedConcurrencyLevelTaskScheduler(Math.Max(Environment.ProcessorCount - 1, 1));
            var workerPool = new LimitedConcurrencyLevelTaskScheduler(1);
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

            var findNeedleBlock = new TransformBlock<NeedleKey, NeedleKey>(key =>
            {
                Console.WriteLine("Finding needle: {0} {1}", key.Key, key.Gravity);
                state.needles.GetOrAdd(key, (k) =>
                {
                    return FindHighEntropyStrip(state.Image(k.Key), key.Gravity);
                });
                Console.WriteLine("Found needle: {0} {1}", key.Key, key.Gravity);
                return key;
            }, blockOptions);

            var findJoinBlock = new TransformBlock<Tuple<string, NeedleKey>, string>(t =>
            {
                Console.WriteLine("Finding join: {0} {1}", t.Item1, t.Item2.Key);
                state.joins.GetOrAdd(t.Item1, (k1) =>
                {
                    var needle = t.Item2;
                    Point? potentialAnchor = null;
                    state.needles.TryGetValue(needle, out potentialAnchor);

                    if (!potentialAnchor.HasValue)
                    {
                        throw new InvalidOperationException($"no needle found for {needle}");
                    }
                    var anchor = potentialAnchor.Value;

                    var needleImage = state.Image(t.Item2.Key).Clone();
                    int NeedleSize = 50; // TODO: Move this needle image cropping back into FindNeedle Task
                    needleImage.Crop((int)anchor.X, (int)anchor.Y, NeedleSize, 1);

                    // TODO: Use gravity to speed up
                    var joinPoint = FindAnchorInImage(state.Image(t.Item2.Key), state.Image(k1));
                    Console.WriteLine("Found join: {0} {1} {2}", t.Item1, t.Item2.Key, joinPoint);

                    return new JoinData()
                    {
                        Source = k1,
                        Target = needle.Key,
                        Join = joinPoint
                    };
                });
                return t.Item1; // TODO: Figure out best thing to propagate. Maybe when match found?
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
            broadcaster.LinkTo(cartesian.Left);
            findNeedleBlock.LinkTo(cartesian.Right);

            cartesian.LinkTo(findJoinBlock, propagate);

            foreach (var file in sourceFiles)
            {
                headBlock.Post(file);
            }
            headBlock.Complete();

            await findJoinBlock.Completion.ContinueWith(_ => Console.WriteLine("Pipeline Finished"));
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


            potentialJoinPoint = await cache(keyForImage("join", image1, needle), () => FindAnchorInImage(needle, image1));

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

        private Point? FindAnchorInImage(IMagickImage needleImage, IMagickImage haystack)
        {
            // Search in bottom strip for anchor
            var d = 50;
            var pixels = haystack.GetPixels();
            var needle = needleImage.GetPixels().Select(i => i.ToColor()).ToList();

            for (var y = haystack.Height - 1; y >= 0; y--)
            {
                List<MagickColor> pixelStrip = Enumerable.Range(0, haystack.Width).Select(i => pixels.GetPixel(i, y).ToColor()).ToList();
                for (var x = 0; x < haystack.Width - needle.Count; x++)
                {
                    var target = Enumerable.Range(x, needle.Count).Select(i => pixelStrip.ElementAt(i));

                    var result = needle.Zip(target, (first, second) => Tuple.Create(first, second)).All(t =>
                    {
                        return t.Item1.FuzzyEquals(t.Item2, new Percentage(10));
                    });

                    if (result)
                    {
                        var ret = new Point(x, y);
                        Console.WriteLine($"FOUND MATCH: ({x}, {y})");
                        return ret;
                    }
                }
            }
            return null;
        }

        public IEnumerable<int> FromTo(int from, int to)
        {
            return Enumerable.Range(from, to - from - 1);
        }

        private Point? FindHighEntropyStrip(IMagickImage image, Gravity gravity)
        {
            var NeedleSize = 50;
            var pixels = image.GetPixels();

            var t1 = DateTime.UtcNow;

            IEnumerable<int> rows = null;
            IEnumerable<int> columns = null;

            Debug.Assert(image.Height > 1 && image.Width > 1, "Assumes non-empty image");
            Debug.Assert(image.Width >= NeedleSize, "Assumes image is at least as big as needle size");

            // TODO: Make search radiate out from center
            switch (gravity)
            {
                case Gravity.South:
                    rows = FromTo(image.Height - image.Height / 3, image.Height).Reverse();
                    columns = FromTo(0, image.Width - NeedleSize);
                    break;
                case Gravity.North:
                    rows = FromTo(0, image.Height / 3);
                    columns = FromTo(0, image.Width - NeedleSize);
                    break;
                case Gravity.East:
                    rows = FromTo(0, image.Height);
                    columns = FromTo(image.Width - image.Width / 3, image.Width - NeedleSize).Reverse();
                    break;
                case Gravity.West:
                    rows = FromTo(0, image.Height);
                    columns = FromTo(0, image.Width - image.Width / 3 - NeedleSize);
                    break;

            }
            Console.WriteLine("{0}x{1}", image.Width, image.Height);
            Console.WriteLine("rows: {0},{1}", rows.Min(), rows.Max());
            Console.WriteLine("columns: {0},{1}", columns.Min(), columns.Max());

            foreach (var y in rows)
            {
                List<float> pixelStrip = FromTo(columns.Min(), columns.Max() + NeedleSize + 1+ 2).Select(i => pixels.GetPixel(i, y).ToColor().ToColor().GetBrightness()).ToList();

                foreach (var x in columns)
                {
                    //Console.WriteLine(x);
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
