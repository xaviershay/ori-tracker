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
        public int totalSearchTasks = 0;
        public int completedSearchTasks = 0;

        public async Task UpdateUI()
        {
            while (true)
            {
                await Task.Delay(500);
                await this.Dispatcher.InvokeAsync(() =>
                {
                    OverallProgress.Maximum = totalSearchTasks;
                    OverallProgress.Value = completedSearchTasks;
                });
            }
        }

        public static void WriteAllTextWithBackup(string path, string contents)
        {
            // generate a temp filename
            // Assume we're only dealing with a single filesystem
            var tempPath = System.IO.Path.GetTempFileName();

            // create the backup name
            var backup = path + ".backup";

            // delete any existing backups
            if (File.Exists(backup))
                File.Delete(backup);

            // get the bytes
            var data = Encoding.UTF8.GetBytes(contents);

            // write the data to a temp file
            using (var tempFile = File.Create(tempPath, 4096, FileOptions.WriteThrough))
                tempFile.Write(data, 0, data.Length);

            if (!File.Exists(backup))
            {
                File.WriteAllText(backup, "");
            }

            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
            }

            // replace the contents
            File.Replace(tempPath, path, backup);
        }

        public async Task DoNetwork()
        {
            OpenCL.IsEnabled = false;
            var cacheFile = System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\stitch_cache.json";

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


            var workerPool = new LimitedConcurrencyLevelTaskScheduler(Math.Max(Environment.ProcessorCount - 2, 1)); // / 2, 1));
            //var workerPool = new LimitedConcurrencyLevelTaskScheduler(1);
            var snapshotState = new ActionBlock<State>((s) =>
            {
                var content = "";
                s.Lock(lockedState => content = JsonConvert.SerializeObject(lockedState));
                WriteAllTextWithBackup(cacheFile, content);
                this.Dispatcher.Invoke(() => Joins.ItemsSource = AppState.Joins);
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

            //var sourceDir = "C:/Users/Xavier/Source/ori-tracker/MapStitcher/Screenshots";
            var sourceDir = "C:/Users/Xavier/Source/ori-tracker/MapStitcher/4KScreenshots";
            /*
            IMagickImage image1 = new MagickImage(System.IO.Path.GetFullPath($"{sourceDir}/../Temp/forlorn-1.png"));
            image1 = image1.Clone();
            var settings = new MorphologySettings
            {
                Channels = Channels.Alpha,
                Method = MorphologyMethod.Distance,
                Kernel = Kernel.Euclidean,
                KernelArguments = "1,50!"
            };

            image1.Alpha(AlphaOption.Set);
            image1.VirtualPixelMethod = VirtualPixelMethod.Transparent;
            image1.Morphology(settings);
            image1.Write(System.IO.Path.GetFullPath($"{sourceDir}/../Temp/forlorn-test.png"));
            */

            /*
            MagickImage image1 = new MagickImage($"{sourceDir}/sorrow-1.png");
            MagickImage image2 = new MagickImage($"{sourceDir}/sorrow-2.png");
            */

            /*
            var sourceFiles = new List<string>
            {
                $"{sourceDir}\\387290_20180314160604_1.png",
            };
            */
            var sourceFiles = Directory.GetFiles(sourceDir, "*.png");
            /*
            var sourceFiles = new List<string>
            {
                $"{sourceDir}\\forlorn-1.png",
                $"{sourceDir}\\forlorn-2.png",
                $"{sourceDir}\\forlorn-3.png",
            };
            */
            //state.ClearJoins();
            /*
            state.ClearNeedle(new NeedleKey { Key = $"{sourceDir}/forlorn-3.png", Gravity = Gravity.West });
            state.ClearJoin($"{sourceDir}/forlorn-2.png", $"{sourceDir}/forlorn-3.png");
            state.ClearJoin($"{sourceDir}/forlorn-1.png", $"{sourceDir}/forlorn-3.png");
            state.ClearJoin($"{sourceDir}/forlorn-2.png", $"{sourceDir}/forlorn-1.png");
            */
            this.Dispatcher.Invoke(() => SourceImages.ItemsSource = SourceImages2.ItemsSource = sourceFiles);
            this.Dispatcher.Invoke(() => Joins.ItemsSource = AppState.Joins);
            UpdateUI();

            var loadFromDiskBlock = new TransformBlock<string, string>(path =>
            {
                // TODO: Make this a load and crop task
                var task = new StitchTask($"Load and crop {System.IO.Path.GetFileName(path)}");
                this.Dispatcher.Invoke(() => Tasks.Add(task));
                state.GetOrAddImage(path, () =>
                {
                    var image = new MagickImage(path);
                    var originalSize = new Size(image.Width, image.Height);
                    int sideMargin = (int)(image.Width * 0.15); // The sides have a subtle animated mask over them. 280px wide on 1920px resolution. Crop them out.
                    int topMargin = (int)(image.Height * 0.17);
                    int bottomMargin = (int)(image.Height * 0.15);
                    var bounds = new MagickGeometry(sideMargin, topMargin, image.Width - sideMargin * 2, image.Height - bottomMargin - topMargin);
                    image.Crop(bounds);
                    image.RePage();
                    //image.Write("C:\\Users\\Xavier\\Source\\ori-tracker\\MapStitcher\\Temp\\" + System.IO.Path.GetFileName(path));
                    return image;
                });
                task.Complete("Done", false);
                return path;
            }, blockOptions);

            var gravities = new TransformManyBlock<string, NeedleKey>(path =>
            {
                return allGravities.Select(g => new NeedleKey() { Key = path, Gravity = g });
            }, blockOptions);

            var findNeedleBlock = new TransformBlock<NeedleKey, NeedleKey>(needle =>
            {
                var task = new FindNeedleTask(state, needle);
                this.Dispatcher.Invoke(() => Tasks.Add(task));
                task.Run();
                return needle;
            }, blockOptions);

            var findJoinBlock = new TransformBlock<SearchKey, string>(t =>
            {
                var haystack = t.Item1;
                var needle = t.Item2;

                var task = new SearchTask(state, haystack, needle);
                this.Dispatcher.Invoke(() => Tasks.Add(task));

                task.Run();

                completedSearchTasks++;
                return haystack; // TODO: Figure out best thing to propagate. Maybe when match found?
            }, blockOptions);

            var broadcaster = new BroadcastBlock<string>(null);
            var cartesian = new CartesianProductBlock<string, NeedleKey>();

            var propagate = new DataflowLinkOptions { PropagateCompletion = true };
            var headBlock = loadFromDiskBlock;
            headBlock.LinkTo(broadcaster, propagate);
            broadcaster.LinkTo(gravities, propagate);
            gravities.LinkTo(findNeedleBlock, propagate);

            // Don't propagate completion from left/right sources for cartesian join. It should
            // complete when _both_ are done (which is it's default behaviour)
            broadcaster.LinkTo(cartesian.Left, propagate);
            findNeedleBlock.LinkTo(cartesian.Right, propagate);

            var countTotals = new TransformManyBlock<Tuple<string, NeedleKey>, SearchKey>(t =>
            {
                var haystack = t.Item1;
                var needle = t.Item2;
                var none = Enumerable.Empty<SearchKey>();

                if (haystack == needle.Key || !state.GetNeedle(needle).MeetsThreshold())
                {
                    return none;
                }

                var existingJoins = state.Joins;
                var connectedJoins = new HashSet<HashSet<string>>();

                foreach (var join in existingJoins)
                {
                    var found = false;
                    foreach (var connectedSubset in connectedJoins)
                    {
                        if (connectedSubset.Contains(join.Image1) || connectedSubset.Contains(join.Image2))
                        {
                            connectedSubset.Add(join.Image1);
                            connectedSubset.Add(join.Image2);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        var newSubset = new HashSet<string>();
                        newSubset.Add(join.Image1);
                        newSubset.Add(join.Image2);
                        connectedJoins.Add(newSubset);
                    }
                }
                connectedJoins.Aggregate(new HashSet<HashSet<string>>(), (acc, x) => {
                    var found = false;
                    foreach (var connectedSubset in acc)
                    {
                        if (connectedSubset.Overlaps(x))
                        {
                            connectedSubset.UnionWith(x);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        acc.Add(x);
                    }
                    return acc;
                });

                if (connectedJoins.Any(x => x.Contains(haystack) && x.Contains(needle.Key)))
                {
                    Console.WriteLine("Two images already connected via transitive joins, skipping");
                    return none;
                }
                totalSearchTasks++;
                return Enumerable.Repeat(SearchKey.Create(t.Item1, t.Item2), 1);
            }, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1
            });

            cartesian.LinkTo(countTotals, propagate);
            countTotals.LinkTo(findJoinBlock, propagate);

            var sink = new ActionBlock<string>(s => { });
            findJoinBlock.LinkTo(sink, propagate);

            foreach (var file in sourceFiles)
            {
                headBlock.Post(file);
            }
            headBlock.Complete();

            await sink.Completion.ContinueWith(_ =>
            {
                Console.WriteLine("Pipeline Finished");

                /*
                this.Dispatcher.Invoke(() => TaskGrid.ItemsSource = Tasks.Where(x =>
                {
                    if (x is SearchTask)
                    {
                        var task = (SearchTask)x;
                        return task.Name.Contains("Found");
                        return task.searchResult.MeetsThreshold();
                        return task.searchResult.Distance < 2500;
                    }
                    return false;
                }));
                */
                Dictionary<string, Point> completedJoins = new Dictionary<string, Point>();

                var remainingJoins = new List<State.Join>(state.Joins);
                var rejects = new List<State.Join>();
                var images = new MagickImageCollection();
                var lastCycleCount = 0;

                var morphologySettings = new MorphologySettings
                {
                    Channels = Channels.Alpha,
                    Method = MorphologyMethod.Distance,
                    Kernel = Kernel.Euclidean,
                    KernelArguments = "1,50!"
                };

                while (remainingJoins.Count > 0 && remainingJoins.Count != lastCycleCount)
                {
                    lastCycleCount = remainingJoins.Count;
                    foreach (var join in remainingJoins)
                    {
                        if (completedJoins.Count == 0)
                        {
                            var tempPath = System.IO.Path.GetTempFileName();
                            var tempPath2 = System.IO.Path.GetTempFileName();
                            // Initial seed
                            var i1 = state.Image(join.Image1).Clone();
                            var i2 = state.Image(join.Image2).Clone();

                            i1.Alpha(AlphaOption.Set);
                            i1.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                            i1.Morphology(morphologySettings);
                            i1.Write(tempPath);
                            i1.Dispose();
                            i1 = new MagickImage(tempPath);
                            i1.BackgroundColor = new MagickColor(18, 18, 18);

                            i2.Alpha(AlphaOption.Set);
                            i2.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                            i2.Morphology(morphologySettings);
                            i2.Write(tempPath2);
                            i2.Dispose();
                            i2 = new MagickImage(tempPath2);

                            i2.Page = new MagickGeometry($"{ToOffset(join.JoinPoint.X)}{ToOffset(join.JoinPoint.Y)}");
                            images.Add(i1);
                            images.Add(i2);

                            completedJoins.Add(join.Image1, new Point(0, 0));
                            completedJoins.Add(join.Image2, join.JoinPoint);
                            File.Delete(tempPath);
                            File.Delete(tempPath2);
                        }
                        else
                        {
                            Point offset = join.JoinPoint;
                            if (completedJoins.ContainsKey(join.Image1) && completedJoins.ContainsKey(join.Image2))
                            {
                                // NOOP
                            } else if (completedJoins.ContainsKey(join.Image1))
                            {
                                completedJoins.TryGetValue(join.Image1, out offset);

                                var tempPath = System.IO.Path.GetTempFileName();
                                var i2 = state.Image(join.Image2).Clone();
                                var joinPoint = new Point(join.JoinPoint.X + offset.X, join.JoinPoint.Y + offset.Y);
                                i2.Alpha(AlphaOption.Set);
                                i2.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                                i2.Morphology(morphologySettings);
                                i2.Write(tempPath);
                                //i2.Dispose();
                                //i2 = new MagickImage(tempPath);
                                i2.Page = new MagickGeometry($"{ToOffset(joinPoint.X)}{ToOffset(joinPoint.Y)}");
                                images.Add(i2);
                                File.Delete(tempPath);
                                completedJoins.Add(join.Image2, joinPoint);
                            }
                            else if (completedJoins.ContainsKey(join.Image2))
                            {
                                completedJoins.TryGetValue(join.Image2, out offset);

                                var tempPath = System.IO.Path.GetTempFileName();
                                var i1 = state.Image(join.Image1).Clone();
                                var joinPoint = new Point(offset.X - join.JoinPoint.X, offset.Y - join.JoinPoint.Y);

                                i1.Alpha(AlphaOption.Set);
                                i1.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                                i1.Morphology(morphologySettings);
                                i1.Write(tempPath);
                                //i1.Dispose();
                                //i1 = new MagickImage(tempPath);

                                i1.Page = new MagickGeometry($"{ToOffset(joinPoint.X)}{ToOffset(joinPoint.Y)}");

                                images.Add(i1);
                                File.Delete(tempPath);
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
                if (images.Any())
                {
                    var merged = images.Merge();

                    //merged.BackgroundColor = new MagickColor(0, 0, 0);
                    //merged.Alpha(AlphaOption.Remove);
                    //merged.Alpha(AlphaOption.Off);
                    merged.Write("C:\\Users\\Xavier\\Source\\ori-tracker\\MapStitcher\\Temp\\map.png");
                    DisplayImage(Viewer, merged);
                    Console.WriteLine("Done Compositing");
                }
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
            // HACK: Wait a second for the initial cached to be populated, otherwise this takes ages.
            Task.Delay(3000).ContinueWith(_ =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    var viewer = GetScrollViewer(TaskGrid);
                    Tasks.CollectionChanged += (sender, args) =>
                    {
                        if (viewer.VerticalOffset >= viewer.ScrollableHeight - 1)
                        {
                            TaskGrid.ScrollIntoView(Tasks.Last());
                        }
                    };
                });
            });
            Task.Run(() => DoNetwork());
        }

        private void Viewer_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition((IInputElement)sender);
            //var p2 = Viewer.PointFromScreen(p);
            //this.Title = $"{p2.X},{p2.Y}";
        }

        private void SourceImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var lv = (ListView)sender;
            var selected = (string)lv.SelectedItem;

            if (selected != null)
            {
                var image = AppState.Image(selected);
                if (image != null)
                {
                    Joins.ItemsSource = AppState.Joins.Where(x => x.Image1 == selected || x.Image2 == selected);
                    image = image.Clone();
                    Task.Run(() => {
                        var needles = allGravities.Select(x => AppState.GetNeedle(new NeedleKey { Key = selected, Gravity = x })).Where(x => x != null && x.MeetsThreshold()).Select(x => x.Point);
                        foreach (var needlePoint in needles)
                        {
                            var rect = new Drawables()
                              .StrokeColor(new MagickColor("red"))
                              .StrokeWidth(2)
                              .FillOpacity(new Percentage(0))
                              .Rectangle(needlePoint.X, needlePoint.Y, needlePoint.X + NeedleSize, needlePoint.Y + NeedleSize);
                            image.Draw(rect);
                        }
                        image.Resize(new Percentage(50));
                        if (lv == SourceImages)
                        {
                            DisplayImage(Viewer, image);
                        } else
                        {
                            DisplayImage(Viewer2, image);
                        }
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

                    var resize = 0.25;
                    var resizeAmount = new Percentage(resize * 100);
                    image1 = image1.Clone();
                    var settings = new MorphologySettings
                    {
                        Channels = Channels.Alpha,
                        Method = MorphologyMethod.Distance,
                        Kernel = Kernel.Euclidean,
                        KernelArguments = "1,20!"
                    };

                    /*
                    image1.Alpha(AlphaOption.Set);
                    image1.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                    image1.Morphology(settings);
                    */

                    image2 = image2.Clone();
                    /*
                    image2.Alpha(AlphaOption.Set);
                    image2.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                    image2.Morphology(settings);
                    */

                    var images = new MagickImageCollection();
                    image2.Page = new MagickGeometry($"{ToOffset(join.JoinPoint.X)}{ToOffset(join.JoinPoint.Y)}");
                    images.Add(image1);
                    images.Add(image2);
                    var result = images.Merge();
                    result.Resize(resizeAmount);

                    DisplayImage(Viewer, result);
                    //DisplayImage(Viewer, image2);
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

        private void ClearCache_Button_Click(object sender, RoutedEventArgs e)
        {
            StitchTask task = (StitchTask)((Button)sender).DataContext;
            task.ClearCache();
            Task.Run(() => task.Run());
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var haystack = (string)SourceImages.SelectedItem;
            var needle = (string)SourceImages2.SelectedItem;

            var tasks = allGravities.Select(gravity => new SearchTask(AppState, haystack, new NeedleKey() { Key = needle, Gravity = gravity }));

            foreach (var task in tasks)
            {
                if (AppState.GetNeedle(task.needle).MeetsThreshold())
                {
                    Tasks.Add(task);
                    Task.Run(() => task.Run());
                }
            }
        }

        private void DeleteSource_Click(object sender, RoutedEventArgs e)
        {
            var haystack = (string)SourceImages.SelectedItem;

            AppState.Delete(haystack);
            File.Delete(haystack);
        }
    }
}
