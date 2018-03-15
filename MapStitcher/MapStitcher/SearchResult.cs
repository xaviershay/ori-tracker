using System.Windows;

namespace MapStitcher
{
    public class SearchResult
    {
        internal static readonly SearchResult Null = new SearchResult();
        public Point HaystackPoint;
        public Point NeedlePoint;
        public double Distance;

        public static double MAX_DISTANCE = 100000000.0;

        public bool MeetsThreshold()
        {
            return Distance < 400;
        }

        internal Point Offset()
        {
            return new Point(HaystackPoint.X - NeedlePoint.X, HaystackPoint.Y - NeedlePoint.Y);
        }
    }
}
