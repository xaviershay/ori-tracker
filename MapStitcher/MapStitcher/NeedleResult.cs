using System.Windows;

namespace MapStitcher
{
    public class NeedleResult
    {
        public Point Point;
        public double Entropy;

        public bool MeetsThreshold()
        {
            return Entropy > 0.005;
        }
    }
}