﻿using System.Windows;

namespace MapStitcher
{
    public class SearchResult
    {
        public static SearchResult Null
        {
            get { return new SearchResult() { Distance = MAX_DISTANCE }; }
        }
        public Point HaystackPoint;
        public Point NeedlePoint;
        public double Distance;

        public static double MAX_DISTANCE = 100000000.0;

        public bool MeetsThreshold()
        {
            return Distance < 200;
        }

        internal Point Offset()
        {
            return new Point(HaystackPoint.X - NeedlePoint.X, HaystackPoint.Y - NeedlePoint.Y);
        }
    }
}
