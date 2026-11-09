using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace VKonvert
{
    /// <summary>
    /// HEIC through Windows' own image codecs (WIC). ImageMagick can't save HEIC because the
    /// HEVC encoder is patent-licensed; Windows ships one with the HEIF/HEVC extensions instead.
    /// </summary>
    public static class HeicWindows
    {
        static readonly Guid HeifContainer = new Guid("E1E62521-6787-405B-A339-500715B5763F");

        public const string MissingCodecMessage =
            "Saving HEIC needs Microsoft’s “HEIF Image Extensions” and “HEVC Video Extensions” " +
            "from the Microsoft Store. Many PCs already have them; if yours doesn’t, install both and try again.";

        public static void Write(byte[] png, string outPath)
        {
            BitmapEncoder encoder;
            try
            {
                encoder = BitmapEncoder.Create(HeifContainer);
            }
            catch (NotSupportedException)
            {
                throw new NotSupportedException(MissingCodecMessage);
            }

            using (var ms = new MemoryStream(png))
            {
                var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                encoder.Frames.Add(frame);
                var temp = outPath + ".tmp";
                try
                {
                    using (var fs = File.Create(temp)) encoder.Save(fs);
                    File.Move(temp, outPath);
                }
                catch (Exception ex) when (!(ex is IOException))
                {
                    if (File.Exists(temp)) File.Delete(temp);
                    throw new NotSupportedException(MissingCodecMessage, ex);
                }
            }
        }

        // Fallback reader for HEIC files ImageMagick can't decode.
        public static byte[] ReadAsPng(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
                using (var ms = new MemoryStream())
                {
                    png.Save(ms);
                    return ms.ToArray();
                }
            }
        }
    }
}
