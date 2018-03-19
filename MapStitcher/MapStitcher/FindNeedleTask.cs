using ImageMagick;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Diagnostics;

namespace MapStitcher
{
    internal class FindNeedleTask : StitchTask
    {
        private double NeedleSize = 100; // TODO: DI and DRY this
        private State state;
        private NeedleKey needle;

        NeedleResult searchResult = null;

        public FindNeedleTask(State state, NeedleKey needle)
        {
            this.state = state;
            this.needle = needle;
            Name = $"Finding needle {needle}";
        }

        public override void ShowPreview(Renderer renderer)
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

            if (searchResult != null)
            {
                var needleRect = new Drawables()
                    .StrokeWidth(2)
                    .StrokeColor(new MagickColor(searchResult.MeetsThreshold() ? "green" : "red"))
                    .FillOpacity(new Percentage(0))
                    .Rectangle(searchResult.Point.X, searchResult.Point.Y, searchResult.Point.X + NeedleSize, searchResult.Point.Y + NeedleSize);
                preview.Draw(needleRect);
            }

            renderer.DisplayImages(preview);
        }

        public override void Run()
        {
            var cached = true;
            this.searchResult = state.GetOrAddNeedle(needle, () =>
            {
                cached = false;
                var image = state.Image(needle.Key).Clone();
                var magnification = 0.2;
                image.Resize(new Percentage(magnification * 100));

                var result = FindHighEntropyStrip(image, needle.Gravity, NeedleSize * magnification, this);

                result.Point.X /= magnification;
                result.Point.Y /= magnification;
                return result;
            });

            var resultLabel = $"Not found ({searchResult.Entropy})";
            if (searchResult.MeetsThreshold())
            {
                resultLabel = $"Found at ({searchResult.Point}), {searchResult.Entropy}";
            }
            Complete(resultLabel, cached);
        }

        internal override void ClearCache()
        {
            base.ClearCache();
            state.ClearNeedle(needle);
            Reset();
        }

        private NeedleResult FindHighEntropyStrip(IMagickImage image, Gravity gravity, double NeedleSize, StitchTask task)
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

            List<List<Pixel>> pixelGrid = LinqExtensions.FromTo(minY, maxY).Select(y => LinqExtensions.FromTo(minX, maxX).Select(x => pixels.GetPixel(x, y)).ToList()).ToList();
            List<List<float>> brightnessGrid = pixelGrid.Select(xs => xs.Select(p => p.ToColor().ToColor().GetBrightness()).ToList()).ToList();

            var gridWidth = maxX - minX;
            var gridHeight = maxY - minY;

            var bestNeedleStddev = 0.0;
            Point bestNeedle = default(Point);

            double totalCycles = rows.Count() * columns.Count();
            double currentCycle = 0;

            Console.WriteLine(brightnessGrid);
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
                    double blackCount = 0.0;
                    for (var x2 = x - minX; x2 < x - minX + NeedleSize; x2++)
                    {
                        for (var y2 = y - minY; y2 < y - minY + NeedleSize; y2++)
                        {
                            var b = brightnessGrid[y2][x2];
                            var p = pixelGrid[y2][x2].ToColor();
                            
                            if (b < 0.08)
                            {
                                blackCount++;
                            }

                            count++;
                            var delta = b - mean;
                            mean = mean + delta / count;
                            var delta2 = b - mean;
                            m2 = m2 + delta * delta2;
                        }
                    }
                    var variance = m2 / (count - 1);
                    var stddev = variance;

                    //Console.WriteLine("{0}, {1}, {2}", blackCount, NeedleSize * NeedleSize, blackCount / (NeedleSize * NeedleSize));
                    if (stddev > bestNeedleStddev && blackCount / (NeedleSize * NeedleSize) < 0.5)
                    {
                        bestNeedleStddev = stddev;
                        bestNeedle = new Point(x, y);
                    }
                }
            }

            return new NeedleResult() { Point = bestNeedle, Entropy = bestNeedleStddev };
        }
        private Tuple<List<int>, List<int>> NeedleSearchArea(IMagickImage image, Gravity gravity)
        {
            var verticalMargin = image.Height / 4;
            var horizontalMargin = image.Height / 3;

            switch (gravity)
            {
                case Gravity.South:
                    return Tuple.Create(
                      LinqExtensions.FromTo(image.Height - verticalMargin, image.Height).AsEnumerable().Reverse().ToList(),
                      LinqExtensions.FromTo(horizontalMargin, image.Width - horizontalMargin).OrderFromCenter().ToList()
                    );
                case Gravity.North:
                    return Tuple.Create(
                        LinqExtensions.FromTo(0, verticalMargin).ToList(),
                        LinqExtensions.FromTo(horizontalMargin, image.Width - horizontalMargin).OrderFromCenter().ToList()
                    );
                case Gravity.East:
                    return Tuple.Create(
                        LinqExtensions.FromTo(verticalMargin, image.Height - verticalMargin).OrderFromCenter().ToList(),
                        LinqExtensions.FromTo(image.Width - horizontalMargin, image.Width).AsEnumerable().Reverse().ToList()
                    );
                case Gravity.West:
                    return Tuple.Create(
                        LinqExtensions.FromTo(verticalMargin, image.Height - verticalMargin).OrderFromCenter().ToList(),
                        LinqExtensions.FromTo(0, horizontalMargin).ToList()
                    );
                default:
                    throw new ArgumentException($"Unhandled gravity: {gravity}");
            }
        }
    }
}