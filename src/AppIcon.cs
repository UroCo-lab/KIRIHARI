using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Kirihari
{
    internal static class AppIcon
    {
        internal static void Apply(Window window)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kirihari.Icon.png"))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 256;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                window.Icon = image;
            }
        }
    }
}
