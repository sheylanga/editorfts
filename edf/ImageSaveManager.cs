using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace edf
{
    public static class ImageSaveManager
    {
        private const int GpsLatitudeRefId = 0x0001;
        private const int GpsLongitudeRefId = 0x0003;
        private const int GpsLatitudeId = 0x0002;
        private const int GpsLongitudeId = 0x0004;

        public static void SaveImageWithOptionalGpsData(Bitmap bitmap, string filePath, double latitude, double longitude)
        {
            var format = DetermineImageFormat(filePath);

            if (HasValidCoordinates(latitude, longitude))
            {
                try
                {
                    if (format == ImageFormat.Png)
                    {
                        SavePngWithGpsData(bitmap, filePath, latitude, longitude);
                    }
                    else
                    {
                        SaveJpegWithGpsData(bitmap, filePath, latitude, longitude);
                    }
                    ShowSuccessMessage("Imagen guardada con datos GPS correctamente.");
                }
                catch (Exception ex)
                {
                    SaveImageWithoutMetadata(bitmap, filePath, format);
                    ShowWarningMessage($"Imagen guardada sin datos GPS.");
                }
            }
            else
            {
                SaveImageWithoutMetadata(bitmap, filePath, format);
                ShowSuccessMessage("Imagen guardada correctamente.");
            }
        }

        private static ImageFormat DetermineImageFormat(string filePath)
        {
            return filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg
                : ImageFormat.Png;
        }

        private static bool HasValidCoordinates(double latitude, double longitude)
        {
            return latitude != 0 || longitude != 0;
        }

        private static void SaveImageWithoutMetadata(Bitmap bitmap, string filePath, ImageFormat format)
        {
            bitmap.Save(filePath, format);
        }

        private static void SavePngWithGpsData(Bitmap bitmap, string filePath, double latitude, double longitude)
        {
            // Guardar PNG normalmente
            bitmap.Save(filePath, ImageFormat.Png);
            
            // Inyectar EXIF GPS directamente en el archivo PNG
            try
            {
                PngExifInjector.InjectGpsExif(filePath, latitude, longitude);
            }
            catch
            {
                // Si falla la inyección, la imagen PNG ya está guardada sin GPS
            }
        }

        private static void SaveJpegWithGpsData(Bitmap bitmap, string filePath, double latitude, double longitude)
        {
            using var bitmapCopy = (Bitmap)bitmap.Clone();
            AddGpsMetadata(bitmapCopy, latitude, longitude);
            bitmapCopy.Save(filePath, ImageFormat.Jpeg);
        }

        private static void AddGpsMetadata(Bitmap bitmap, double latitude, double longitude)
        {
            try
            {
                AddGpsLatitudeReference(bitmap, latitude);
                AddGpsLongitudeReference(bitmap, longitude);
                AddGpsLatitudeValue(bitmap, latitude);
                AddGpsLongitudeValue(bitmap, longitude);
            }
            catch
            {
                // Error silencioso - al menos la imagen se guardó
            }
        }

        private static void AddGpsLatitudeReference(Bitmap bitmap, double latitude)
        {
            try
            {
                var item = CreatePropertyItem(GpsLatitudeRefId);
                item.Type = 2;
                item.Value = System.Text.Encoding.ASCII.GetBytes(latitude >= 0 ? "N\0" : "S\0");
                item.Len = item.Value.Length;
                bitmap.SetPropertyItem(item);
            }
            catch { }
        }

        private static void AddGpsLongitudeReference(Bitmap bitmap, double longitude)
        {
            try
            {
                var item = CreatePropertyItem(GpsLongitudeRefId);
                item.Type = 2;
                item.Value = System.Text.Encoding.ASCII.GetBytes(longitude >= 0 ? "E\0" : "W\0");
                item.Len = item.Value.Length;
                bitmap.SetPropertyItem(item);
            }
            catch { }
        }

        private static void AddGpsLatitudeValue(Bitmap bitmap, double latitude)
        {
            try
            {
                var item = CreatePropertyItem(GpsLatitudeId);
                item.Type = 5;
                item.Value = GpsMetadataManager.ConvertCoordinateToExifFormat(Math.Abs(latitude));
                item.Len = item.Value.Length;
                bitmap.SetPropertyItem(item);
            }
            catch { }
        }

        private static void AddGpsLongitudeValue(Bitmap bitmap, double longitude)
        {
            try
            {
                var item = CreatePropertyItem(GpsLongitudeId);
                item.Type = 5;
                item.Value = GpsMetadataManager.ConvertCoordinateToExifFormat(Math.Abs(longitude));
                item.Len = item.Value.Length;
                bitmap.SetPropertyItem(item);
            }
            catch { }
        }

        private static PropertyItem CreatePropertyItem(int id)
        {
            var constructor = typeof(PropertyItem).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            var item = (PropertyItem)constructor!.Invoke(null);
            item.Id = id;
            return item;
        }

        private static void ShowSuccessMessage(string message)
        {
            MessageBox.Show(message, "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void ShowWarningMessage(string message)
        {
            MessageBox.Show(message, "Advertencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
