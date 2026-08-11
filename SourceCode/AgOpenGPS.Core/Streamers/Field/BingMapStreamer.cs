using AgLibrary.Logging;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Models;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AgOpenGPS.Core.Streamers
{
    public class BingMapStreamer : FieldAspectStreamer
    {
        private readonly BingMapBitmapStreamer _bitmapStreamer;
        public BingMapStreamer() : base("BackPic.txt", null)
        {
            _bitmapStreamer = new BingMapBitmapStreamer();
        }

        /// <summary>JPEG quality the background is saved with (Low ~75, High ~88).</summary>
        public long JpegQuality
        {
            get { return _bitmapStreamer.JpegQuality; }
            set { _bitmapStreamer.JpegQuality = value; }
        }

        public BingMap TryRead(DirectoryInfo fieldDirectory)
        {
            BingMap bingMap = null;
            FileInfo fileInfo = GetFileInfo(fieldDirectory);
            if (fileInfo.Exists)
            {
                try
                {
                    bingMap = Read(fieldDirectory);
                }
                catch (Exception)
                {
                }
            }
            return bingMap;
        }

        public void TryWrite(BingMap bingMap, DirectoryInfo fieldDirectory)
        {
            try
            {
                Write(bingMap, fieldDirectory);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\n Cannot write to file.");
                Log.EventWriter("Saving BingMap" + e.ToString());
            }
        }

        private BingMap Read(DirectoryInfo fieldDirectory)
        {
            BingMap bingMap = null;
            FileInfo fileInfo = GetFileInfo(fieldDirectory);
            using (GeoStreamReader reader = new GeoStreamReader(fileInfo))
            {
                string line = reader.ReadLine(); // skip header
                bool hasBingMap = reader.ReadBool();
                if (hasBingMap)
                {
                    GeoBoundingBox geoBb = reader.ReadGeoBoundingBox();
                    Bitmap bitmap = _bitmapStreamer.Read(fieldDirectory);
                    if (bitmap != null)
                    {
                        bingMap = new BingMap(geoBb, bitmap);
                    }
                }
            }
            return bingMap;
        }

        private void Write(BingMap bingMap, DirectoryInfo fieldDirectory)
        {
            if (bingMap != null)
            {
                FileInfo boundingBoxFileInfo = GetFileInfo(fieldDirectory);
                using (GeoStreamWriter writer = new GeoStreamWriter(boundingBoxFileInfo))
                {
                    writer.WriteLine("$BackPic");
                    writer.WriteBool(true);
                    writer.WriteGeoBoundingBox(bingMap.GeoBoundingBox);
                }
                _bitmapStreamer.Write(bingMap.Bitmap, fieldDirectory);
            }
            else
            {
                DeleteFile(fieldDirectory);
                _bitmapStreamer.DeleteFile(fieldDirectory);
            }
        }

        public void CreateFile(DirectoryInfo fieldDirectory)
        {
            fieldDirectory.Create();
            using (StreamWriter writer = new StreamWriter(GetFileInfo(fieldDirectory).Name))
            {
            }
        }

        /// <summary>
        /// The captured background, stored as JPEG (2026-08-10). A field background is a
        /// photograph, and at the sizes the Map App now captures (up to 4096 px on the long
        /// side) PNG costs ~30 MB per field and a second or two to decode on a tablet, for no
        /// visible gain. There is deliberately NO fallback to the old BackPic.png: a field
        /// recorded with one simply opens without a background and is re-captured once.
        /// </summary>
        private class BingMapBitmapStreamer : FieldAspectStreamer
        {
            //quality of the saved JPEG; the Map App picks it from the Low/High setting
            public long JpegQuality = 85L;

            public BingMapBitmapStreamer() : base("BackPic.jpg", null)
            {
            }

            public Bitmap Read(DirectoryInfo fieldDirectory)
            {
                Bitmap bitmap = null;
                FileInfo fileInfo = GetFileInfo(fieldDirectory);
                if (fileInfo.Exists)
                {
                    bitmap = new Bitmap(Image.FromFile(fileInfo.FullName));
                }
                return bitmap;
            }

            public void Write(Bitmap bitmap, DirectoryInfo fieldDirectory)
            {
                FileInfo fileInfo = GetFileInfo(fieldDirectory);
                if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }
                if (bitmap == null) return;

                ImageCodecInfo jpeg = null;
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                {
                    if (codec.FormatID == ImageFormat.Jpeg.Guid) { jpeg = codec; break; }
                }

                if (jpeg == null)
                {
                    bitmap.Save(fileInfo.FullName, ImageFormat.Jpeg);
                    return;
                }

                using (EncoderParameters pars = new EncoderParameters(1))
                {
                    pars.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                    bitmap.Save(fileInfo.FullName, jpeg, pars);
                }
            }
        }
    }
}
