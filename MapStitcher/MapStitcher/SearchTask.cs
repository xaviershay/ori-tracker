using ImageMagick;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;

namespace MapStitcher
{
    internal class SearchTask : StitchTask
    {
        private string haystack;
        private NeedleKey needle;
        private State state;

        private IMagickImage NeedleImage;
        private int NeedleSize = 100; // TODO: DI this
        private SearchResult searchResult;
        private IEnumerable<Point> initialCandidates;

        public SearchTask(State state, string haystack, NeedleKey needle)
        {
            this.haystack = haystack;
            this.needle = needle;
            this.state = state;
            Name = $"Searching {System.IO.Path.GetFileName(haystack)} for {needle}";
        }

        public override void Run()
        {
            var cached = false;

            var needleResult = state.GetNeedle(needle);
            Point? potentialAnchor = null;
            if (needleResult.MeetsThreshold())
            {
                potentialAnchor = needleResult.Point;
            }

            if (!potentialAnchor.HasValue)
            {
                throw new ArgumentException("Needle does not exist but should");
            }
            var anchor = potentialAnchor.Value;
            var needleImage = state.Image(needle.Key).Clone();
            needleImage.Crop((int)anchor.X, (int)anchor.Y, NeedleSize, NeedleSize);
            NeedleImage = needleImage;

            cached = true;
            this.searchResult = state.GetOrAddSearch(haystack, needle, () =>
            {
                cached = false;
                var result = FindTemplateInImage(NeedleImage, needle.Gravity, state.Image(haystack), this, anchor);
                return result;
            });

            string resultLabel = $"Not found (best: {this.searchResult.Distance})";
            if (this.searchResult.MeetsThreshold())
            {
                resultLabel = $"Found at ({this.searchResult.HaystackPoint}), distance {this.searchResult.Distance}";
            }
            Complete(resultLabel, cached);
        }

        public override void ShowPreview(Renderer renderer)
        {
            if (NeedleImage != null)
            {
                var pixelMagnification = 1.0;
                var magnification = Math.Min(1 / (double)pixelMagnification, 1.0);
                var resizeAmount = new Percentage(magnification * 100);

                IMagickImage newHaystack = null;
                var haystack = this.state.Image(this.haystack);
                lock (haystack) {
                    newHaystack = haystack.Clone();
                    newHaystack.Resize(resizeAmount);
                };
                haystack = newHaystack;

                if (this.initialCandidates != null)
                {
                    foreach(var candidate in this.initialCandidates)
                    {
                        var rect = new Drawables()
                          .StrokeWidth(1)
                          .StrokeColor(new MagickColor("blue"))
                          .FillOpacity(new Percentage(0))
                          .Rectangle(candidate.X * magnification, candidate.Y * magnification, (candidate.X + NeedleSize) * magnification, (candidate.Y + NeedleSize) * magnification);
                        haystack.Draw(rect);
                    }
                }

                if (this.searchResult != null && this.searchResult.Distance < SearchResult.MAX_DISTANCE)
                {
                    var joinPoint = this.searchResult.HaystackPoint;
                    var x = joinPoint.X;
                    var y = joinPoint.Y;

                    haystack.Crop((int)(x * magnification), (int)(y * magnification), (int)(NeedleSize * magnification), (int)(NeedleSize * magnification));
                    var rect = new Drawables()
                      .StrokeWidth(1)
                      .StrokeColor(searchResult.MeetsThreshold() ? new MagickColor("green") : new MagickColor("yellow"))
                      .FillOpacity(new Percentage(0))
                      .Rectangle(x * magnification, y * magnification, (x + NeedleSize) * magnification, (y + NeedleSize) * magnification);
                    haystack.Draw(rect);
                }
                var needle = NeedleImage.Clone();
                needle.Resize(resizeAmount);
                renderer.DisplayImages(haystack, needle);
            }
        }
        private SortedList<double, Point> FindTemplateCandidates(IMagickImage searchArea, IMagickImage template, MagickGeometry bounds, IProgress<double> progress, double threshold, HashSet<Point> tried)
        {
            Console.WriteLine("Searching an area {0}x{1} in {4}x{5} for {2}x{3}", bounds.Width, bounds.Height, template.Width, template.Height, searchArea.Width, searchArea.Height);
            var templatePixels = ToPixels(template);
            var searchPixels = ToPixels(searchArea);

            var candidates = new SortedList<double, Point>(new DuplicateKeyComparer<double>());

            double totalCycles = (bounds.Width - template.Width) * (bounds.Height - template.Height);
            double currentCycles = 0;

            for (var y = bounds.Y; y < bounds.Y + bounds.Height - templatePixels.Count; y++)
            {
                var row = searchPixels[y];
                for (var x = bounds.X; x < bounds.X + bounds.Width - templatePixels.First().Count; x++)
                {
                    var point = new Point(x, y);
                    if (!tried.Contains(point))
                    {
                        var sumOfDistance = 0.0;
                        var totalComparisons = 0.0;

                        var m2 = 0.0;
                        var mean = 0.0;
                        var count = 0;

                        for (var y2 = 0; y2 < templatePixels.Count; y2++)
                        {
                            var templateRow = templatePixels[y2];

                            for (var x2 = 0; x2 < templateRow.Count; x2++)
                            {
                                var distance = PixelDistance(searchPixels[y + y2][x + x2], templateRow[x2]);

                                count++;
                                var delta = distance - mean;
                                mean = mean + delta / count;
                                var delta2 = distance = mean;
                                m2 = m2 + delta * delta2;

                                sumOfDistance += distance;
                                totalComparisons++;
                            }
                        }

                        var averageDistance = sumOfDistance / totalComparisons;

                        var variance = m2 / (count - 1);
                        if (averageDistance <= threshold && Math.Abs(variance) < 100000)
                        {
                            candidates.Add(averageDistance, point);
                        }
                        tried.Add(point);
                    }

                    progress.Report(currentCycles / totalCycles);
                    currentCycles++;
                }
            }
            return candidates;
        }

        internal override void ClearCache()
        {
            base.ClearCache();

            state.ClearSearch(haystack, needle);

            Reset();

            initialCandidates = null;
        }

        // TODO: Name of this method + signature is a mess
        private SearchResult FindTemplateInImage(IMagickImage needleImage, Gravity needleGravity, IMagickImage haystack, StitchTask task, Point anchor)
        {
            // Resize needle 
            var pixelMagnification = 8.0;
            var magnification = Math.Min(1 / (double)pixelMagnification, 1.0);
            var progress = (IProgress<double>)task;

            var resizeAmount = new Percentage(magnification * 100);
            var template = needleImage.Clone();
            template.Resize(resizeAmount);
            template.RePage();

            IMagickImage searchArea = null;
            var resized = false;
            while (!resized)
            {
                try
                {
                    lock (haystack)
                    {
                        searchArea = haystack.Clone();
                        searchArea.Resize(resizeAmount);
                        searchArea.RePage();
                    }
                    resized = true;
                } catch (AccessViolationException)
                {
                    Console.WriteLine("Corrupt Memory, trying again");
                    Thread.Sleep(500);
                }
            }

            // We need to get the actual values here, since Resize() has unexpected rounding logic
            // (only supports whole digit percentages) and if we don't use the actual values then
            // scaling math won't work properly.
            var oldWidth = searchArea.Width;
            var oldHeight = searchArea.Height;

            var bounds = new MagickGeometry(0, 0, searchArea.Width, searchArea.Height);
            var candidates = FindTemplateCandidates(searchArea, template, bounds, progress, 1000, new HashSet<Point>());

            if (candidates.Any())
            {
                var bestScore = candidates.First().Key;
                this.initialCandidates = candidates.Where(x => x.Key < bestScore * 1.1).Select(x => {
                    return new Point(x.Value.X / magnification, x.Value.Y / magnification);
                }).ToList();
            }


            while (pixelMagnification > 1 && candidates.Any())
            {
                var newCandidates = new SortedList<double, Point>(new DuplicateKeyComparer<double>());
                var newPixelMagnification = pixelMagnification / 2;
                var newMagnification = Math.Min(1 / (double)newPixelMagnification, 1.0);
                var newResizeAmount = new Percentage(newMagnification * 100);
                var threshold = 2000.0;
                var bestSeen = threshold;
                var bestScore = candidates.First().Key;
                var toLoop = candidates.Where(x => x.Key < bestScore * 1.1);
                Console.WriteLine("Considering {0} candidates at {1}", toLoop.Count(), newMagnification);

                IMagickImage newHaystack = null;
                lock (haystack)
                {
                    newHaystack = haystack.Clone();
                    newHaystack.Resize(newResizeAmount);
                    newHaystack.RePage();
                }

                var t2 = needleImage.Clone();
                t2.Resize(newResizeAmount);
                t2.RePage();

                var cache = new HashSet<Point>();


                foreach (var candidate in toLoop)
                {
                    var point = new Point(candidate.Value.X / oldWidth * newHaystack.Width, candidate.Value.Y / oldHeight * newHaystack.Height);
                    var margin = NeedleSize * newMagnification / 2;

                    var clampedBounds = new MagickGeometry(
                        (int)(point.X - margin),
                        (int)(point.Y - margin),
                        (int)(NeedleSize * newMagnification + margin * 2),
                        (int)(NeedleSize * newMagnification + margin * 2)
                    );
                    clampedBounds.X = Math.Max(0, clampedBounds.X);
                    clampedBounds.Y = Math.Max(0, clampedBounds.Y);
                    clampedBounds.Width = Math.Min(newHaystack.Width - clampedBounds.X, clampedBounds.Width);
                    clampedBounds.Height = Math.Min(newHaystack.Height - clampedBounds.Y, clampedBounds.Height);

                    var toAdd = FindTemplateCandidates(newHaystack, t2, clampedBounds, this, threshold, cache);
                    foreach (var add in toAdd)
                    {
                        newCandidates.Add(add.Key, add.Value);
                        if (add.Key < bestSeen)
                        {
                            bestSeen = add.Key;
                            Console.WriteLine("Updating best score: {0}", bestSeen);
                        }
                    }
                }
                candidates = newCandidates;
                magnification = newMagnification;
                pixelMagnification = newPixelMagnification;
                oldWidth = newHaystack.Width;
                oldHeight = newHaystack.Height;
            }

            Console.WriteLine("============ Final: {0}", candidates.Count);

            if (candidates.Any())
            {
                var bestCandidate = candidates.First();

                return new SearchResult()
                {
                    Distance = bestCandidate.Key,
                    HaystackPoint = bestCandidate.Value,
                    NeedlePoint = anchor
                };
            }
            return SearchResult.Null;
        }

        private List<List<System.Drawing.Color>> ToPixels(IMagickImage image)
        {
            var pixels = image.GetPixels();
            return LinqExtensions.FromTo(0, image.Height).Select(y => LinqExtensions.FromTo(0, image.Width).Select(x => pixels.GetPixel(x, y).ToColor().ToColor()).ToList()).ToList();
        }

        private double PixelDistance(System.Drawing.Color a, System.Drawing.Color b)
        {
            return Math.Pow((a.R - b.R), 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2);
        }

        public class DuplicateKeyComparer<TKey>
                        :
                     IComparer<TKey> where TKey : IComparable
        {
            #region IComparer<TKey> Members

            public int Compare(TKey x, TKey y)
            {
                int result = x.CompareTo(y);

                if (result == 0)
                    return 1;   // Handle equality as beeing greater
                else
                    return result;
            }

            #endregion
        }
    }
}