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


            //var workerPool = new LimitedConcurrencyLevelTaskScheduler(Math.Max(Environment.ProcessorCount / 2, 1));
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
            this.Dispatcher.Invoke(() => SourceImages.ItemsSource = sourceFiles);

            var loadFromDiskBlock = new TransformBlock<string, string>(path =>
            {
                // TODO: Make this a load and crop task
                var task = new StitchTask($"Load and crop {System.IO.Path.GetFileName(path)}");
                this.Dispatcher.Invoke(() => Tasks.Add(task));
                state.GetOrAddImage(path, () =>
                {
                    var image = new MagickImage(path);
                    var originalSize = new Size(image.Width, image.Height);
                    int sideMargin = (int)(image.Width * 0.06); // The sides are darkened, so clip them out.
                    int topMargin = (int)(image.Height * 0.17);
                    int bottomMargin = (int)(image.Height * 0.10);
                    var bounds = new MagickGeometry(sideMargin, topMargin, image.Width - sideMargin * 2, image.Height - bottomMargin - topMargin);
                    image.Crop(bounds);
                    image.RePage();
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

            var findJoinBlock = new TransformBlock<Tuple<string, NeedleKey>, string>(t =>
            {
                var haystack = t.Item1;
                var needle = t.Item2;

                if (haystack == needle.Key || !state.GetNeedle(needle).MeetsThreshold())
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

            cartesian.LinkTo(findJoinBlock, propagate);

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
                Dictionary<string, Point> completedJoins = new Dictionary<string, Point>();

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
                if (images.Any())
                {
                    var merged = images.Merge();
                    DisplayImage(Viewer, merged);
                    this.Dispatcher.Invoke(() => Joins.ItemsSource = AppState.Joins);
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
                        var needles = allGravities.Select(x => AppState.GetNeedle(new NeedleKey { Key = selected, Gravity = x })).Where(x => x.MeetsThreshold()).Select(x => x.Point);
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

                    image1 = image1.Clone();
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

        private void ClearCache_Button_Click(object sender, RoutedEventArgs e)
        {
            StitchTask task = (StitchTask)((Button)sender).DataContext;
            task.ClearCache();
            Task.Run(() => task.Run());
        }
    }
}
