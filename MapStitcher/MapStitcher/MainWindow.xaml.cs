using ImageMagick;
using PersistentObjectCachenet45;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        public MainWindow()
        {
            InitializeComponent();

            Task.Run(async () =>
            {
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
                    return FindHighEntropyStrip(image2);
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
            });
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
            if (!default(T).Equals(loadedInstance))
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

        private Point? FindHighEntropyStrip(MagickImage image2)
        {
            var d = 50;
            var image = image2;
            var pixels = image.GetPixels();

            var t1 = DateTime.UtcNow;

            for (var y = 0; y < image.Height; y++)
            {
                List<float> pixelStrip = Enumerable.Range(0, image.Width).Select(i => pixels.GetPixel(i, y).ToColor().ToColor().GetBrightness()).ToList();

                for (var x = 0; x < image.Width - d; x++)
                {
                    var brightness = Enumerable.Range(x, d).Select(i => pixelStrip.ElementAt(i));

                    var avg = brightness.Average();
                    if (avg > 0.5)
                    {
                        double sum = brightness.Sum(a => Math.Pow(a - avg, 2));
                        var ret = Math.Sqrt((sum) / (brightness.Count() - 1));
                        if (ret > 0.2)
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
