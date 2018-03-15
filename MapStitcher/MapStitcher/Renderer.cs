using System;
using System.Windows.Controls;
using ImageMagick;
using System.Linq;
using System.IO;
using System.Windows.Media.Imaging;

namespace MapStitcher
{
    public class Renderer
    {
        private Image[] viewers;

        public Renderer(params Image[] viewers)
        {
            this.viewers = viewers;

        }
        internal void DisplayImages(params IMagickImage[] magickImages)
        {
            foreach (var t in viewers.Zip(magickImages, (a, b) => Tuple.Create(a, b)))
            {
                DisplayImage(t.Item1, t.Item2);
            }
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

                viewer.Dispatcher.Invoke(() =>
                {
                    viewer.Source = bitmapImage;
                });
            }
        }
    }
}