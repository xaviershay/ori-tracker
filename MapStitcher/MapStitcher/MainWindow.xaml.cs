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
        private int NeedleSize = 50; // TODO: Move this needle image cropping back into FindNeedle Task
        public State AppState { get; private set; }
        private List<Gravity> allGravities = new List<Gravity>()
        {
            Gravity.North,
            Gravity.East,
            Gravity.South,
            Gravity.West
        };

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
            AppState = state;


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

            /*
            var sourceFiles = new List<string>
            {
                $"{sourceDir}/sorrow-1.png",
                $"{sourceDir}/sorrow-2.png",
            };
            */
            var sourceFiles = new List<string>
            {
                $"{sourceDir}/forlorn-1.png",
                $"{sourceDir}/forlorn-2.png",
                $"{sourceDir}/forlorn-3.png",
            };
            //state.ClearNeedle(new NeedleKey { Key = $"{sourceDir}/forlorn-3.png", Gravity = Gravity.West });
            state.ClearJoin($"{sourceDir}/forlorn-2.png", $"{sourceDir}/forlorn-3.png");
            state.ClearJoin($"{sourceDir}/forlorn-1.png", $"{sourceDir}/forlorn-3.png");
            state.ClearJoin($"{sourceDir}/forlorn-2.png", $"{sourceDir}/forlorn-1.png");
            this.Dispatcher.Invoke(() => SourceImages.ItemsSource = sourceFiles);

            var loadFromDiskBlock = new TransformBlock<string, string>(path =>
            {
                state.Image(path);
                return path;
            });

            var cropImagesBlock = new TransformBlock<string, string>(path =>
            {
                // TODO: This is destructive, so that's probably bad in concurrent world?
                var image = state.Image(path);
                var sideMargin = 200; // The sides are darkened, so clip them out.
                var bounds = new MagickGeometry(sideMargin, 370, image.Width - sideMargin * 2, image.Height - 250 - 370);
                image.Crop(bounds);
                image.RePage();
                image.Write("C:/Users/Xavier/Temp/" + System.IO.Path.GetFileName(path));
                return path;
            }, blockOptions);

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

                Console.WriteLine("Message recv: {0} {1}", System.IO.Path.GetFileName(haystack), needle);

                if (haystack == needle.Key)
                {
                    Console.WriteLine("Dropping self-join for {0}", haystack);
                    return haystack; // TODO: This doesn't mean anything
                }
                if (!(System.IO.Path.GetFileName(haystack) == "forlorn-2.png" && needle.ToString() == "forlorn-3.png|West"))
                {
                    Console.WriteLine("Skipping");
                    return haystack;
                }

                if (!state.JoinExists(haystack, needle))
                {
                    Point? potentialAnchor = state.GetNeedle(needle);

                    if (!potentialAnchor.HasValue)
                    {
                        Console.WriteLine("No needle exists for {0}, so no join possible", needle);
                        return null;
                    }
                    var anchor = potentialAnchor.Value;

                    var needleImage = state.Image(needle.Key).Clone();
                    needleImage.Crop((int)anchor.X, (int)anchor.Y, NeedleSize, NeedleSize);

                    var needleViewImage = state.Image(needle.Key).Clone();
                    needleViewImage.Crop((int)anchor.X, (int)anchor.Y-50, NeedleSize, 100);

                    //DisplayImage(Viewer, state.Image(haystack));
                    //DisplayImage(Viewer2, needleViewImage);
                    Console.WriteLine("Searching for {0} in {1}", needle, System.IO.Path.GetFileName(haystack));
                    var joinPoint = FindAnchorInImage(needleImage, needle.Gravity, state.Image(haystack));

                    state.AddJoinPoint(haystack, needle, joinPoint, anchor);

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
                var joins = state.Joins.Where(x => sourceFiles.Contains(x.Image1)).GroupBy(k => k.Image1).ToDictionary(k => k.Key, v => v.ToList());
                if (joins.Count > 0)
                {
                    var seed = joins.First().Key;
                    var candidates = new Queue<string>();
                    candidates.Enqueue(seed);
                    Console.WriteLine(joins.Count);

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
                    this.Dispatcher.Invoke(() => Joins.ItemsSource = AppState.Joins);
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
            AppState = new State();

            Task.Run(() => DoNetwork());
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
            var d = 100;
            var pixels = haystack.GetPixels();
            var needle = needleImage.GetPixels().Select(i => i.ToColor()).ToList();
            var searchArea = SearchArea(haystack, Opposite(needleGravity));
            var rows = searchArea.Item1;
            var columns = searchArea.Item2;

            var minY = rows.Min();
            var maxY = rows.Max();

            var minX = columns.Min();
            var maxX = columns.Max();
            var imageDimensions = Tuple.Create(haystack.Width, haystack.Height);

            var needlePixels = needleImage.GetPixels();
            List<List<MagickColor>> needleGrid = FromTo(0, needleImage.Height).Select(y => FromTo(0, needleImage.Width).Select(x => needlePixels.GetPixel(x, y).ToColor()).ToList()).ToList();
            List<List<MagickColor>> pixelGrid = FromTo(minY, maxY).Select(y => FromTo(minX, maxX).Select(x => pixels.GetPixel(x, y).ToColor()).ToList()).ToList();

            var gridWidth = maxX - minX;
            var gridHeight = maxY - minY;

            var threshold = new Percentage(15);

            var displayHaystack = haystack.Clone();
            displayHaystack.Crop(minX, minY, maxX - minX, maxY - minY);
            DisplayImage(Viewer, displayHaystack);
            DisplayImage(Viewer2, needleImage);

            foreach (var y in rows)
            {
                if (y - minY + NeedleSize >= gridHeight)
                {
                    continue;
                }

                foreach (var x in columns)
                {
                    if (x - minX + NeedleSize >= gridWidth)
                    {
                        continue;
                    }

                    var found = true;

                    for (var x2 = x - minX; x2 < x - minX + NeedleSize && found; x2++)
                    {
                        for (var y2 = y - minY; y2 < y - minY + NeedleSize && found; y2++)
                        {
                            var needlePixel = needleGrid[y2 - (y - minY)][x2 - (x - minX)];
                            var haystackPixel = pixelGrid[y2][x2];

                            if (!needlePixel.FuzzyEquals(haystackPixel, threshold))
                            {
                                found = false;
                            }
                        }
                    }

                    if (found)
                    {
                        var ret = new Point(x, y);
                        Console.WriteLine($"FOUND MATCH: ({x}, {y})");
                        return ret;
                    }
                }
                /*
                this.Dispatcher.Invoke(() =>
                {
                    Progress.Value = Math.Abs((double)(y - Math.Min(rows.Last(), rows.First())) / (double)(rows.Last() - rows.First()) * 100);
                });
                */
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

        public List<int> FromTo(int from, int to)
        {
            return Enumerable.Range(from, to - from).ToList();
        }

        private Tuple<List<int>, List<int>> SearchArea(IMagickImage image, Gravity gravity)
        {
            var margin = 550;
            switch (gravity)
            {
                case Gravity.South:
                    return Tuple.Create(
                      FromTo(image.Height - margin, image.Height).AsEnumerable().Reverse().ToList(),
                      FromTo(margin, image.Width - margin).OrderFromCenter().ToList()
                    );
                case Gravity.North:
                    return Tuple.Create(
                        FromTo(0, margin).ToList(),
                        FromTo(margin, image.Width - margin).OrderFromCenter().ToList()
                    );
                case Gravity.East:
                    return Tuple.Create(
                        FromTo(margin, image.Height).OrderFromCenter().ToList(),
                        FromTo(image.Width - margin, image.Width).AsEnumerable().Reverse().ToList()
                    );
                case Gravity.West:
                    return Tuple.Create(
                        FromTo(margin, image.Height - margin).OrderFromCenter().ToList(),
                        FromTo(0, margin).ToList()
                    );
                default:
                    throw new ArgumentException($"Unhandled gravity: {gravity}");
            }
        }

        private Point? FindHighEntropyStrip(IMagickImage image, Gravity gravity)
        {
            var pixels = image.GetPixels();

            var t1 = DateTime.UtcNow;

            IEnumerable<int> rows = null;
            IEnumerable<int> columns = null;

            Debug.Assert(image.Height > 1 && image.Width > 1, "Assumes non-empty image");
            Debug.Assert(image.Width >= NeedleSize, "Assumes image is at least as big as needle size");

            var searchArea = SearchArea(image, gravity);
            rows = searchArea.Item1;
            columns = searchArea.Item2;

            var minY = rows.Min();
            var maxY = rows.Max();

            var minX = columns.Min();
            var maxX = columns.Max();
            var imageDimensions = Tuple.Create(image.Width, image.Height);

            List<List<Pixel>> pixelGrid = FromTo(minY, maxY).Select(y => FromTo(minX, maxX).Select(x => pixels.GetPixel(x, y)).ToList()).ToList();
            List<List<float>> brightnessGrid = pixelGrid.Select(xs => xs.Select(p => p.ToColor().ToColor().GetBrightness()).ToList()).ToList();

            var gridWidth = maxX - minX;
            var gridHeight = maxY - minY;

            var bestNeedleStddev = 0.0;
            Point? bestNeedle = null;

            foreach (var y in rows)
            {
                if (y - minY + NeedleSize >= gridHeight)
                {
                    continue;
                }

                foreach (var x in columns)
                {
                    if (x - minX + NeedleSize >= gridWidth)
                    {
                        continue;
                    }

                    var count = 0;
                    var mean = 0.0;
                    var m2 = 0.0;
                    /*
  def update(existingAggregate, newValue):
    (count, mean, M2) = existingAggregate
    count = count + 1 
    delta = newValue - mean
    mean = mean + delta / count
    delta2 = newValue - mean
    M2 = M2 + delta * delta2

    return (count, mean, M2)

# retrieve the mean and variance from an aggregate
def finalize(existingAggregate):
    (count, mean, M2) = existingAggregate
    (mean, variance) = (mean, M2/(count - 1)) 
    if count < 2:
        return float('nan')
    else:
        return (mean, variance)
        */
                    for (var x2 = x - minX; x2 < x - minX + NeedleSize; x2++)
                    {
                        for (var y2 = y - minY; y2 < y - minY + NeedleSize; y2++)
                        {
                            var b = brightnessGrid[y2][x2];

                            count++;
                            var delta = b - mean;
                            mean = mean + delta / count;
                            var delta2 = b - mean;
                            m2 = m2 + delta * delta2;
                        }
                    }
                    var variance = m2 / (count - 1);
                    //var swatch = brightnessGrid.GetRange(y, NeedleSize).Select(xs => xs.GetRange(x, NeedleSize)).SelectMany(xs => xs);
                    //var averageBrightness = swatch.Average();
                    //var sum = swatch.Sum(a => Math.Pow(a - averageBrightness, 2));
                    //var stddev = Math.Sqrt(sum / (swatch.Count() - 1));
                    var stddev = variance;

                    if (stddev > bestNeedleStddev)
                    {
                        bestNeedleStddev = stddev;
                        bestNeedle = new Point(x, y);
                    }
                    /*
                    var swatchBrightness = 
                    var brightness = Enumerable.Range(x - columns.Min(), NeedleSize).Select(i => pixelStrip.ElementAt(i));

                    var avg = brightness.Average();
                    if (avg > 0.3)
                    {
                        double sum = brightness.Sum(a => Math.Pow(a - avg, 2));
                        var ret = Math.Sqrt((sum) / (brightness.Count() - 1));
                        Console.WriteLine(ret);
                        if (ret > 0.10)
                        {
                            var r = new Point(x, y);
                            Console.WriteLine($"({x}, {y}): {r}");
                            Console.WriteLine(string.Join(",", brightness.ToList()));
                            var t2 = DateTime.UtcNow;
                            Console.WriteLine(t2 - t1);
                            return r;
                        }
                    }
                    */
                }
            }
            if (bestNeedle.HasValue)
            {
                Console.WriteLine("Found: {0}", bestNeedle.Value);
            }
            return bestNeedle;
        }

        private void Viewer_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition((IInputElement)sender);
            //var p2 = Viewer.PointFromScreen(p);
            //this.Title = $"{p2.X},{p2.Y}";
        }

        private void SourceImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (string)SourceImages.SelectedItem;

            if (selected != null)
            {
                var image = AppState.Image(selected);
                if (image != null)
                {
                    image = image.Clone();
                    Task.Run(() => {
                        var needles = allGravities.Select(x => AppState.GetNeedle(new NeedleKey { Key = selected, Gravity = x })).Where(x => x.HasValue).Select(x => x.Value);
                        foreach (var needlePoint in needles)
                        {
                            var rect = new Drawables()
                              .StrokeColor(new MagickColor("red"))
                              .StrokeWidth(2)
                              .FillOpacity(new Percentage(0))
                              .Rectangle(needlePoint.X, needlePoint.Y, needlePoint.X + NeedleSize, needlePoint.Y + NeedleSize);
                            image.Draw(rect);
                        }
                        DisplayImage(Viewer, image);
                    });
                }
            }
        }

        private void Joins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (State.Join?)Joins.SelectedItem;

            if (selected != null)
            {
                var join = selected.Value;
                var image1 = AppState.Image(join.Image1);
                var image2 = AppState.Image(join.Image2);

                /*
                var newWidth = 10;
                var newHeight = 10;
                */

                Task.Run(() =>
                {
                    image2 = image2.Clone();

                    var images = new MagickImageCollection();
                    image2.Page = new MagickGeometry($"{ToOffset(join.JoinPoint.X)}{ToOffset(join.JoinPoint.Y)}");
                    images.Add(image1);
                    images.Add(image2);
                    var result = images.Merge();

                    DisplayImage(Viewer, result);
                });
            }

        }

        private string ToOffset(double x)
        {
            if (x < 0)
            {
                return $"{(int)x}";
            } else
            {
                return $"+{(int)x}";
            }
        }
    }
}
