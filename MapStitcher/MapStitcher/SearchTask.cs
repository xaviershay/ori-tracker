using ImageMagick;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MapStitcher
{
    internal class SearchTask : StitchTask
    {
        private string haystack;
        private NeedleKey needle;
        private State state;

        private Point? joinPoint;

        private IMagickImage NeedleImage;
        private int NeedleSize = 100; // TODO: DI this

        public SearchTask(State state, string haystack, NeedleKey needle)
        {
            this.haystack = haystack;
            this.needle = needle;
            this.state = state;
            Name = $"Searching {System.IO.Path.GetFileName(haystack)} for {needle}";
        }

        public void Run()
        {
            var cached = false;

            Point? potentialAnchor = state.GetNeedle(needle);

            if (!potentialAnchor.HasValue)
            {
                throw new ArgumentException("Needle does not exist but should");
            }
            var anchor = potentialAnchor.Value;
            var needleImage = state.Image(needle.Key).Clone();
            needleImage.Crop((int)anchor.X, (int)anchor.Y, NeedleSize, NeedleSize);
            NeedleImage = needleImage;

            cached = true;
            this.joinPoint = state.GetOrAddSearch(haystack, needle, () =>
            {
                cached = false;
                return FindAnchorInImage2(NeedleImage, needle.Gravity, state.Image(haystack), this);
            });

            string resultLabel = "Not found";
            if (this.joinPoint.HasValue)
            {
                resultLabel = $"Found at ({this.joinPoint.Value})";
            }
            Complete(resultLabel, cached);
        }

        public override void ShowPreview(Renderer renderer)
        {
            if (NeedleImage != null)
            {
                var haystack = this.state.Image(this.haystack).Clone();

                if (this.joinPoint.HasValue)
                {
                    var x = joinPoint.Value.X;
                    var y = joinPoint.Value.Y;

                    var rect = new Drawables()
                      .StrokeWidth(2)
                      .StrokeColor(new MagickColor("yellow"))
                      .FillOpacity(new Percentage(0))
                      .Rectangle(x, y, x + NeedleSize, y + NeedleSize);
                    haystack.Draw(rect);
                }
                renderer.DisplayImages(haystack, NeedleImage);
            }
        }

        private Point? FindAnchorInImage2(IMagickImage needleImage, Gravity needleGravity, IMagickImage haystack,  StitchTask task)
        {
            // Resize needle 
            var magnification = Math.Min((double)4 / needleImage.Width, 1.0);

            var resizeAmount = new Percentage(magnification * 100);

            var template = needleImage.Clone();
            template.Resize(resizeAmount);

            var searchArea = haystack.Clone();
            searchArea.Resize(resizeAmount);

            var templatePixels = toPixels(template);
            var searchPixels = toPixels(searchArea);

            var candidates = new SortedList<double, Point>(new DuplicateKeyComparer<double>());

            for (var y = 0; y < searchPixels.Count - templatePixels.Count; y++)
            {
                var row = searchPixels[y];
                for (var x = 0; x < row.Count - templatePixels.First().Count; x++)
                {

                    var sumOfDistance = 0.0;
                    var totalComparisons = 0.0;

                    for (var y2 = 0; y2 < templatePixels.Count; y2++)
                    {
                        var templateRow = templatePixels[y2];

                        for (var x2 = 0; x2 < templateRow.Count; x2++)
                        {
                            var distance = PixelDistance(searchPixels[y+y2][x+x2], templateRow[x2]);

                            sumOfDistance += distance;
                            totalComparisons++;
                        }
                    }

                    var percentageDifference = sumOfDistance / totalComparisons;

                    candidates.Add(percentageDifference, new Point(x, y));
                }
            }

            /*
            task.Preview = (renderer) =>
            {
                var preview = searchArea.Clone();

                if (candidates.Count > 0)
                {
                    var max = candidates.Last().Key;
                    var min = candidates.First().Key;

                    foreach (var candidate in candidates.Take(1))
                    {
                        var x = candidate.Value.X;
                        var y = candidate.Value.Y;
                        var whiteAmount = (candidate.Key - min) / (max - min);
//                        preview.Crop((int)x, (int)y, template.Width, template.Height);

                        var rect = new Drawables()
                          .FillColor(new MagickColor((byte)(255 * whiteAmount), 255, (byte)(255 * whiteAmount)))
                          .FillOpacity(new Percentage(0.05))
                          .Rectangle(x, y, x + template.Width, y + template.Height);
                        preview.Draw(rect);
                    }
                }
                renderer.DisplayImages(preview, template);
            };
            */

            var threshold = 400; // TODO: What should this be?

            if (candidates.Count > 0)
            {
                var bestPoint = candidates.First();

                if (bestPoint.Key < threshold)
                {
                    return new Point(bestPoint.Value.X / magnification, bestPoint.Value.Y / magnification);
                } 

            }
            return null;
        }

        private List<List<System.Drawing.Color>> toPixels(IMagickImage image)
        {
            var pixels = image.GetPixels();
            return FromTo(0, image.Height).Select(y => FromTo(0, image.Width).Select(x => pixels.GetPixel(x, y).ToColor().ToColor()).ToList()).ToList();
        }

        public List<int> FromTo(int from, int to)
        {
            return Enumerable.Range(from, to - from).ToList();
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