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
        private int NeedleSize = 100; // TODO: Move this needle image cropping back into FindNeedle Task
        private ObservableCollection<StitchTask> Tasks = new ObservableCollection<StitchTask>();
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

            Console.WriteLine(JsonConvert.DeserializeObject<State>(JsonConvert.SerializeObject(state)));
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


            var workerPool = new LimitedConcurrencyLevelTaskScheduler(Math.Max(Environment.ProcessorCount / 2, 1));
            //var workerPool = new LimitedConcurrencyLevelTaskScheduler(1);
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
                $"{sourceDir}\\forlorn-1.png",
                $"{sourceDir}\\forlorn-2.png",
                $"{sourceDir}\\forlorn-3.png",
            };
            //var sourceFiles = Directory.GetFiles(sourceDir, "*.png");
            /*
            state.ClearNeedle(new NeedleKey { Key = $"{sourceDir}/forlorn-3.png", Gravity = Gravity.West });
            state.ClearJoin($"{sourceDir}/forlorn-2.png", $"{sourceDir}/forlorn-3.png");
            state.ClearJoin($"{sourceDir}/forlorn-1.png", $"{sourceDir}/forlorn-3.png");
            state.ClearJoin($"{sourceDir}/forlorn-2.png", $"{sourceDir}/forlorn-1.png");
            */
            this.Dispatcher.Invoke(() => SourceImages.ItemsSource = sourceFiles);

            var loadFromDiskBlock = new TransformBlock<string, string>(path =>
            {
                state.Image(path);
                return path;
            });

            var cropImagesBlock = new TransformBlock<string, string>(path =>
            {
                var task = new StitchTask($"Crop {System.IO.Path.GetFileName(path)}");
                this.Dispatcher.Invoke(() => Tasks.Add(task));

                // TODO: This is destructive, so that's probably bad in concurrent world?
                var image = state.Image(path);
                var originalSize = new Size(image.Width, image.Height);
                int sideMargin = (int)(image.Width * 0.06); // The sides are darkened, so clip them out.
                int topMargin = (int)(image.Height * 0.17);
                int bottomMargin = (int)(image.Height * 0.10);
                var bounds = new MagickGeometry(sideMargin, topMargin, image.Width - sideMargin * 2, image.Height - bottomMargin - topMargin);
                image.Crop(bounds);
                image.RePage();
                Console.WriteLine("Crop done");

                task.Complete(String.Format("{0} → {1}", originalSize, new Size(image.Width, image.Height)), false);
                return path;
            }, blockOptions);

            var gravities = new TransformManyBlock<string, NeedleKey>(path =>
            {
                return allGravities.Select(g => new NeedleKey() { Key = path, Gravity = g });
            }, blockOptions);

            var findNeedleBlock = new TransformBlock<NeedleKey, NeedleKey>(needle =>
            {
                Point? result = null;
                var cached = false;
                var task = new StitchTask($"Finding needle at {needle}");
                this.Dispatcher.Invoke(() => Tasks.Add(task));
                if (!state.NeedleExists(needle))
                {
                    result = FindHighEntropyStrip(state.Image(needle.Key), needle.Gravity, task);
                    state.AddNeedle(needle, result);
                } else
                {
                    cached = true;
                    result = state.GetNeedle(needle);
                    task.Preview = () =>
                    {
                        var image = state.Image(needle.Key).Clone();
                        var searchArea = NeedleSearchArea(image, needle.Gravity);
                        var rows = searchArea.Item1;
                        var columns = searchArea.Item2;

                        var minX = columns.Min();
                        var maxX = columns.Max();
                        var minY = rows.Min();
                        var maxY = rows.Max();

                        var preview = image;
                        var rect = new Drawables()
                          .StrokeWidth(2)
                          .StrokeColor(new MagickColor("yellow"))
                          .FillOpacity(new Percentage(0))
                          .Rectangle(minX, minY, maxX, maxY);
                        preview.Draw(rect);

                        if (result.HasValue)
                        {
                            var needleRect = new Drawables()
                              .StrokeWidth(2)
                              .StrokeColor(new MagickColor("red"))
                              .FillOpacity(new Percentage(0))
                              .Rectangle(result.Value.X, result.Value.Y, result.Value.X + NeedleSize, result.Value.Y + NeedleSize);
                            preview.Draw(needleRect);
                        }
                        DisplayImage(Viewer, preview);
                    };
                }
                var resultLabel = "Not found";
                if (result.HasValue)
                {
                    resultLabel = $"Found at ({result})";
                }
                task.Complete(resultLabel, cached);
                return needle;
            }, blockOptions);

            var findJoinBlock = new TransformBlock<Tuple<string, NeedleKey>, string>(t =>
            {
                var haystack = t.Item1;
                var needle = t.Item2;

                if (haystack == needle.Key || state.GetNeedle(needle) == null)
                {
                    return haystack; // TODO: This doesn't mean anything
                }
                /*
                if (!(System.IO.Path.GetFileName(haystack) == "forlorn-2.png" && needle.ToString() == "forlorn-1.png|North"))
                {
                    Console.WriteLine("Skipping");
                    return haystack;
                }
                */

                var task = new SearchTask(state, haystack, needle);
                this.Dispatcher.Invoke(() => Tasks.Add(task));

                task.Run();
                /*
                if (!state.JoinExists(haystack, needle))
                {
                    Point? potentialAnchor = state.GetNeedle(needle);

                    if (!potentialAnchor.HasValue)
                    {
                        Console.WriteLine("No needle exists for {0}, so no join possible", needle);
                        task.Complete("Needle doesn't exist", true);
                        return null;
                    }

                    var anchor = potentialAnchor.Value;

                    var needleImage = state.Image(needle.Key).Clone();
                    needleImage.Crop((int)anchor.X, (int)anchor.Y, NeedleSize, NeedleSize);


                    //DisplayImage(Viewer, state.Image(haystack));
                    //DisplayImage(Viewer2, needleViewImage);
                    Console.WriteLine("Searching for {0} in {1}", needle, System.IO.Path.GetFileName(haystack));
                    result = FindAnchorInImage2(needleImage, needle.Gravity, state.Image(haystack), task);

                    state.AddJoinPoint(haystack, needle, result, anchor);

                } else
                {
                    cached = true;
                    result = state.GetJoin(haystack, needle);
                }
                string resultLabel = "Not found";
                if (result.HasValue)
                {
                    resultLabel = $"Found at ({result.Value})";
                }
                task.Complete(resultLabel, cached);
                */

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

            await sink.Completion.ContinueWith(async _ =>
            {
                snapshotState.Complete();
                await snapshotState.Completion;
                Console.WriteLine("Pipeline Finished");
                Dictionary<string, Point> completedJoins = new Dictionary<string, Point>();

                //var remainingJoins = state.Joins.Where(x => sourceFiles.Contains(x.Image1)).GroupBy(k => k.Image1).ToDictionary(k => k.Key, v => v.ToList()).ToList();
                var remainingJoins = new List<State.Join>(state.Joins);
                var rejects = new List<State.Join>();
                var images = new MagickImageCollection();
                var lastCycleCount = 0;

                while (remainingJoins.Count > 0 && remainingJoins.Count != lastCycleCount)
                {
                    lastCycleCount = remainingJoins.Count;
                    foreach (var join in remainingJoins)
                    {
                        if (completedJoins.Count == 0)
                        {
                            // Initial seed
                            var i1 = state.Image(join.Image1).Clone();
                            var i2 = state.Image(join.Image2).Clone();
                            i2.Page = new MagickGeometry($"{ToOffset(join.JoinPoint.X)}{ToOffset(join.JoinPoint.Y)}");
                            images.Add(i1);
                            images.Add(i2);

                            completedJoins.Add(join.Image1, new Point(0, 0));
                            completedJoins.Add(join.Image2, join.JoinPoint);
                        }
                        else
                        {
                            Point offset = join.JoinPoint;
                            if (completedJoins.ContainsKey(join.Image1) && completedJoins.ContainsKey(join.Image2))
                            {
                                // NOOP
                                //throw new Exception("Just curious what causes this");
                            } else if (completedJoins.ContainsKey(join.Image1))
                            {
                                completedJoins.TryGetValue(join.Image1, out offset);

                                var i2 = state.Image(join.Image2).Clone();
                                var joinPoint = new Point(join.JoinPoint.X + offset.X, join.JoinPoint.Y + offset.Y);
                                i2.Page = new MagickGeometry($"{ToOffset(joinPoint.X)}{ToOffset(joinPoint.Y)}");
                                images.Add(i2);
                                completedJoins.Add(join.Image2, joinPoint);
                            }
                            else if (completedJoins.ContainsKey(join.Image2))
                            {
                                completedJoins.TryGetValue(join.Image2, out offset);

                                var i1 = state.Image(join.Image1).Clone();
                                var joinPoint = new Point(offset.X - join.JoinPoint.X, offset.Y - join.JoinPoint.Y);
                                i1.Page = new MagickGeometry($"{ToOffset(joinPoint.X)}{ToOffset(joinPoint.Y)}");
                                images.Add(i1);
                                completedJoins.Add(join.Image1, joinPoint);
                            }
                            else
                            {
                                rejects.Add(join);
                            }
                        }
                    }
                    remainingJoins = rejects.ToList();
                    rejects.Clear();
                }
                var merged = images.Merge();
                DisplayImage(Viewer, merged);
                this.Dispatcher.Invoke(() => Joins.ItemsSource = AppState.Joins);
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
        // https://stackoverflow.com/questions/41132649/get-datagrids-scrollviewer
        public static ScrollViewer GetScrollViewer(UIElement element)
        {
            if (element == null) return null;

            ScrollViewer retour = null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element) && retour == null; i++) {
                if (VisualTreeHelper.GetChild(element, i) is ScrollViewer) {
                    retour = (ScrollViewer) (VisualTreeHelper.GetChild(element, i));
                }
                else {
                    retour = GetScrollViewer(VisualTreeHelper.GetChild(element, i) as UIElement);
                }
            }
            return retour;
        }
        public MainWindow()
        {
            InitializeComponent();
            AppState = new State();

            TaskGrid.ItemsSource = Tasks;
            // Implement "sticky" scrolling. If the control is scrolled to the bottom, keep it that
            // way when new items are added. Otherwise, don't change the scroll position.
            Tasks.CollectionChanged += (sender, args) =>
            {
                var viewer = GetScrollViewer(TaskGrid);
                if (viewer.VerticalOffset >= viewer.ScrollableHeight - 1)
                {
                    TaskGrid.ScrollIntoView(Tasks.Last());
                }
            };
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


        private Point? FindAnchorInImage(IMagickImage needleImage, Gravity needleGravity, IMagickImage haystack, StitchTask task)
        {
            IProgress<double> progress = task;
            haystack = haystack.Clone();
            needleImage = needleImage.Clone();

            haystack.Resize(new Percentage(25));
            needleImage.Resize(new Percentage(25));

            var pixels = haystack.GetPixels();
            var needle = needleImage.GetPixels().Select(i => i.ToColor()).ToList();
            var searchArea = HaystackSearchArea(haystack, Opposite(needleGravity));
            var rows = searchArea.Item1;
            var columns = searchArea.Item2;

            /*
            rows = FromTo(500, 600);
            columns = FromTo(200, 300);
            */
            var minY = rows.Min();
            var maxY = rows.Max();

            var minX = columns.Min();
            var maxX = columns.Max();

            var imageDimensions = Tuple.Create(haystack.Width, haystack.Height);

            task.Preview = () =>
            {
                var previewImage = haystack.Clone();

                var rect = new Drawables()
                  .StrokeColor(new MagickColor("yellow"))
                  .StrokeWidth(2)
                  .FillOpacity(new Percentage(10))
                  .Rectangle(minX, minY, maxX, maxY);
                previewImage.Draw(rect);
                DisplayImage(Viewer, previewImage);
                DisplayImage(Viewer2, needleImage);
            };

            var needlePixels = needleImage.GetPixels();
            List<List<System.Drawing.Color>> needleGrid = FromTo(0, needleImage.Height).Select(y => FromTo(0, needleImage.Width).Select(x => needlePixels.GetPixel(x, y).ToColor().ToColor()).ToList()).ToList();
            List<List<System.Drawing.Color>> pixelGrid = FromTo(minY, maxY).Select(y => FromTo(minX, maxX).Select(x => pixels.GetPixel(x, y).ToColor().ToColor()).ToList()).ToList();

            var gridWidth = maxX - minX;
            var gridHeight = maxY - minY;

            /*
            var displayHaystack = haystack.Clone();
            displayHaystack.Crop(minX, minY, maxX - minX, maxY - minY);
            DisplayImage(Viewer, displayHaystack);
            DisplayImage(Viewer2, needleImage);
            */

            double totalCycles = rows.Count() * columns.Count();
            double currentCycle = 0;

            foreach (var y in rows)
            {
                progress.Report(currentCycle / totalCycles);

                foreach (var x in columns)
                {
                    currentCycle++;

                    if (y - minY + NeedleSize >= gridHeight)
                    {
                        continue;
                    }

                    if (x - minX + NeedleSize >= gridWidth)
                    {
                        continue;
                    }

                    var found = true;
                    var matches = 0;

                    for (var x2 = x - minX; x2 < x - minX + NeedleSize && found; x2++)
                    {
                        for (var y2 = y - minY; y2 < y - minY + NeedleSize && found; y2++)
                        {
                            var needlePixel = needleGrid[y2 - (y - minY)][x2 - (x - minX)];
                            var haystackPixel = pixelGrid[y2][x2];

                            if (!FuzzyEquals(needlePixel, haystackPixel, Math.Pow(255 * 0.15, 2)))
                            {
                                //found = false;
                            } else
                            {
                                matches++;
                            }
                        }
                    }

                    if (matches / Math.Pow(NeedleSize, 2) > 0.80)
                    {
                        progress.Report(1.0);
                        var ret = new Point(x, y);
                        Console.WriteLine($"FOUND MATCH: ({x}, {y})");

                        task.Preview = () =>
                        {
                            var previewImage = haystack.Clone();
                            previewImage.Crop(x, y, NeedleSize, NeedleSize);
                            DisplayImage(Viewer, previewImage);
                            DisplayImage(Viewer2, needleImage);
                        };
                        return ret;
                    }
                }
            }
            progress.Report(1.0);
            return null;
        }

        private bool FuzzyEquals(System.Drawing.Color a, System.Drawing.Color b, double thresholdSquared)
        {
            var vectorDistanceSquared = Math.Pow((a.R - b.R), 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2);
            return vectorDistanceSquared <= thresholdSquared;
        }

        private double PixelDistance(System.Drawing.Color a, System.Drawing.Color b)
        {
            return Math.Pow((a.R - b.R), 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2);
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

        private Tuple<List<int>, List<int>> NeedleSearchArea(IMagickImage image, Gravity gravity)
        {
            var verticalMargin = image.Height / 4;
            var horizontalMargin = image.Height / 4;

            switch (gravity)
            {
                case Gravity.South:
                    return Tuple.Create(
                      FromTo(image.Height - verticalMargin, image.Height).AsEnumerable().Reverse().ToList(),
                      FromTo(horizontalMargin, image.Width - horizontalMargin).OrderFromCenter().ToList()
                    );
                case Gravity.North:
                    return Tuple.Create(
                        FromTo(0, verticalMargin).ToList(),
                        FromTo(horizontalMargin, image.Width - horizontalMargin).OrderFromCenter().ToList()
                    );
                case Gravity.East:
                    return Tuple.Create(
                        FromTo(verticalMargin, image.Height - verticalMargin).OrderFromCenter().ToList(),
                        FromTo(image.Width - horizontalMargin, image.Width).AsEnumerable().Reverse().ToList()
                    );
                case Gravity.West:
                    return Tuple.Create(
                        FromTo(verticalMargin, image.Height - verticalMargin).OrderFromCenter().ToList(),
                        FromTo(0, horizontalMargin).ToList()
                    );
                default:
                    throw new ArgumentException($"Unhandled gravity: {gravity}");
            }
        }

        private Tuple<List<int>, List<int>> HaystackSearchArea(IMagickImage image, Gravity gravity)
        {
            switch (gravity)
            {
                case Gravity.South:
                    return Tuple.Create(
                      FromTo(image.Height / 2, image.Height).AsEnumerable().Reverse().ToList(),
                      FromTo(0, image.Width).OrderFromCenter().ToList()
                    );
                case Gravity.North:
                    return Tuple.Create(
                        FromTo(0, image.Height / 2).ToList(),
                        FromTo(0, image.Width).OrderFromCenter().ToList()
                    );
                case Gravity.East:
                    return Tuple.Create(
                        FromTo(0, image.Height).OrderFromCenter().ToList(),
                        FromTo(image.Width / 2, image.Width).AsEnumerable().Reverse().ToList()
                    );
                case Gravity.West:
                    return Tuple.Create(
                        FromTo(0, image.Height).OrderFromCenter().ToList(),
                        FromTo(0, image.Width / 2).ToList()
                    );
                default:
                    throw new ArgumentException($"Unhandled gravity: {gravity}");
            }
        }

        private Point? FindHighEntropyStrip(IMagickImage image, Gravity gravity, StitchTask task)
        {
            IProgress<double> progress = task;
            var pixels = image.GetPixels();

            var t1 = DateTime.UtcNow;

            IEnumerable<int> rows = null;
            IEnumerable<int> columns = null;

            Debug.Assert(image.Height > 1 && image.Width > 1, "Assumes non-empty image");
            Debug.Assert(image.Width >= NeedleSize, "Assumes image is at least as big as needle size");

            var searchArea = NeedleSearchArea(image, gravity);
            rows = searchArea.Item1;
            columns = searchArea.Item2;

            var minY = rows.Min();
            var maxY = rows.Max();

            var minX = columns.Min();
            var maxX = columns.Max();
            var imageDimensions = Tuple.Create(image.Width, image.Height);

            task.Preview = () =>
            {
                var preview = image.Clone();
                var rect = new Drawables()
                  .StrokeWidth(2)
                  .StrokeColor(new MagickColor("yellow"))
                  .FillOpacity(new Percentage(0))
                  .Rectangle(minX, minY, maxX, maxY);
                preview.Draw(rect);
                DisplayImage(Viewer, preview);
            };

            List<List<Pixel>> pixelGrid = FromTo(minY, maxY).Select(y => FromTo(minX, maxX).Select(x => pixels.GetPixel(x, y)).ToList()).ToList();
            List<List<float>> brightnessGrid = pixelGrid.Select(xs => xs.Select(p => p.ToColor().ToColor().GetBrightness()).ToList()).ToList();

            var gridWidth = maxX - minX;
            var gridHeight = maxY - minY;

            var bestNeedleStddev = 0.0;
            Point? bestNeedle = null;

            double totalCycles = rows.Count() * columns.Count();
            double currentCycle = 0;

            foreach (var y in rows)
            {
                foreach (var x in columns)
                {
                    progress.Report(currentCycle / totalCycles);
                    currentCycle++;
                    if (y - minY + NeedleSize >= gridHeight)
                    {
                        continue;
                    }

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
            if (bestNeedle.HasValue && bestNeedleStddev > 0.03)
            {
                Console.WriteLine("Found: {0} @ {1} for {2}", bestNeedle.Value, bestNeedleStddev, gravity);
                task.Preview = () =>
                {
                    var preview = image.Clone();
                    var rect = new Drawables()
                      .StrokeWidth(2)
                      .StrokeColor(new MagickColor("yellow"))
                      .FillOpacity(new Percentage(0))
                      .Rectangle(minX, minY, maxX, maxY);
                    preview.Draw(rect);

                    var needleRect = new Drawables()
                      .StrokeWidth(2)
                      .StrokeColor(new MagickColor("red"))
                      .FillOpacity(new Percentage(0))
                      .Rectangle(bestNeedle.Value.X, bestNeedle.Value.Y, bestNeedle.Value.X + NeedleSize, bestNeedle.Value.Y + NeedleSize);
                    preview.Draw(needleRect);
                    DisplayImage(Viewer, preview);
                };
                return bestNeedle;
            } else
            {
                return null;
            }
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

        private void TaskGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (StitchTask)TaskGrid.SelectedItem;

            if (selected != null)
            {
                // TODO: Need to put on background thread
                var renderer = new Renderer(Viewer, Viewer2);
                selected.ShowPreview(renderer);
            }
        }
    }
}
