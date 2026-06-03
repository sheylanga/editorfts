using System;
using System.IO;
using System.Text;

namespace edf
{
    /// <summary>
    /// Inyecta metadatos EXIF GPS directamente en archivos PNG
    /// Mantiene la extensión .png y modifica solo los metadatos internos
    /// </summary>
    public static class PngExifInjector
    {
        private const string ExifMarker = "Exif\0\0";

        public static void InjectGpsExif(string pngFilePath, double latitude, double longitude)
        {
            if (!File.Exists(pngFilePath))
                throw new FileNotFoundException($"Archivo PNG no encontrado: {pngFilePath}");

            try
            {
                var pngBytes = File.ReadAllBytes(pngFilePath);
                var modifiedBytes = AddExifChunkToPng(pngBytes, latitude, longitude);
                File.WriteAllBytes(pngFilePath, modifiedBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error inyectando EXIF en PNG", ex);
            }
        }

        private static byte[] AddExifChunkToPng(byte[] pngBytes, double latitude, double longitude)
        {
            // Verificar firma PNG
            if (!IsPngFile(pngBytes))
                throw new InvalidOperationException("No es un archivo PNG válido");

            var exifData = BuildExifData(latitude, longitude);
            return InsertExifChunkBeforeIend(pngBytes, exifData);
        }

        private static bool IsPngFile(byte[] data)
        {
            return data.Length >= 8 &&
                   data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                   data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
        }

        private static byte[] BuildExifData(double latitude, double longitude)
        {
            using var ms = new MemoryStream();

            // Encabezado Exif: "Exif\0\0"
            ms.Write(Encoding.ASCII.GetBytes(ExifMarker), 0, 6);

            // TIFF Header (Big-endian)
            ms.Write(new byte[] { 0x4D, 0x4D }, 0, 2); // "MM"
            WriteBigEndian(ms, (ushort)42); // Magic number TIFF

            // Offset al primer IFD
            WriteBigEndian(ms, (uint)8);

            // IFD - GPS Sub-IFD Pointer
            var gpsIfdOffset = 26u;
            WriteIfd(ms, 0x8825, gpsIfdOffset); // GPS IFD Pointer

            // GPS Sub-IFD
            WriteGpsSubIfd(ms, latitude, longitude);

            return ms.ToArray();
        }

        private static void WriteIfd(MemoryStream ms, ushort tag, uint value)
        {
            WriteBigEndian(ms, (ushort)1); // 1 entrada

            WriteBigEndian(ms, tag); // Tag
            WriteBigEndian(ms, (ushort)4); // Type: LONG
            WriteBigEndian(ms, (uint)1); // Count
            WriteBigEndian(ms, value); // Value

            WriteBigEndian(ms, (uint)0); // Siguiente IFD offset
        }

        private static void WriteGpsSubIfd(MemoryStream ms, double latitude, double longitude)
        {
            var latRef = latitude >= 0 ? "N" : "S";
            var lonRef = longitude >= 0 ? "E" : "W";

            using var ifdMs = new MemoryStream();

            // Contar entradas
            WriteBigEndian(ifdMs, (ushort)4); // 4 entradas

            // GPS Latitude Ref
            WriteGpsEntry(ifdMs, 0x0001, 2, 1, Encoding.ASCII.GetBytes(latRef + "\0")[0]);

            // GPS Latitude Value
            var latRationals = CoordinateToRationals(Math.Abs(latitude));
            WriteGpsEntry(ifdMs, 0x0002, 5, 3, BitConverter.ToUInt32(latRationals, 0));

            // GPS Longitude Ref
            WriteGpsEntry(ifdMs, 0x0003, 2, 1, Encoding.ASCII.GetBytes(lonRef + "\0")[0]);

            // GPS Longitude Value
            var lonRationals = CoordinateToRationals(Math.Abs(longitude));
            WriteGpsEntry(ifdMs, 0x0004, 5, 3, BitConverter.ToUInt32(lonRationals, 0));

            WriteBigEndian(ifdMs, (uint)0); // Siguiente IFD

            ms.Write(ifdMs.ToArray(), 0, (int)ifdMs.Length);
        }

        private static void WriteGpsEntry(MemoryStream ms, ushort tag, ushort type, uint count, uint value)
        {
            WriteBigEndian(ms, tag);
            WriteBigEndian(ms, type);
            WriteBigEndian(ms, count);
            WriteBigEndian(ms, value);
        }

        private static byte[] CoordinateToRationals(double coordinate)
        {
            int degrees = (int)coordinate;
            double remainder = (coordinate - degrees) * 60;
            int minutes = (int)remainder;
            double seconds = (remainder - minutes) * 60;

            using var ms = new MemoryStream();

            // Degrees (numerator/denominator)
            WriteBigEndian(ms, (uint)degrees);
            WriteBigEndian(ms, (uint)1);

            // Minutes
            WriteBigEndian(ms, (uint)minutes);
            WriteBigEndian(ms, (uint)1);

            // Seconds
            WriteBigEndian(ms, (uint)(seconds * 1000));
            WriteBigEndian(ms, (uint)1000);

            return ms.ToArray();
        }

        private static byte[] InsertExifChunkBeforeIend(byte[] pngBytes, byte[] exifData)
        {
            using var ms = new MemoryStream();

            // Copiar firma PNG
            ms.Write(pngBytes, 0, 8);

            int pos = 8;
            bool exifInserted = false;

            while (pos < pngBytes.Length - 12)
            {
                // Leer tamaño del chunk (big-endian)
                uint length = ReadBigEndianUint(pngBytes, pos);
                string chunkType = Encoding.ASCII.GetString(pngBytes, pos + 4, 4);

                // Si es IEND, insertar eXIf antes
                if (chunkType == "IEND" && !exifInserted)
                {
                    WriteExifChunk(ms, exifData);
                    exifInserted = true;
                }

                // Copiar chunk actual
                uint chunkSize = length + 12;
                ms.Write(pngBytes, pos, (int)chunkSize);
                pos += (int)chunkSize;
            }

            return ms.ToArray();
        }

        private static void WriteExifChunk(MemoryStream ms, byte[] exifData)
        {
            // Tamaño del chunk (sin incluir tipo y CRC)
            WriteBigEndian(ms, (uint)exifData.Length);

            // Tipo: "eXIf"
            ms.Write(Encoding.ASCII.GetBytes("eXIf"), 0, 4);

            // Datos EXIF
            ms.Write(exifData, 0, exifData.Length);

            // CRC (simplificado a 0 para compatibilidad básica)
            WriteBigEndian(ms, (uint)0);
        }

        private static void WriteBigEndian(MemoryStream ms, ushort value)
        {
            ms.WriteByte((byte)((value >> 8) & 0xFF));
            ms.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteBigEndian(MemoryStream ms, uint value)
        {
            ms.WriteByte((byte)((value >> 24) & 0xFF));
            ms.WriteByte((byte)((value >> 16) & 0xFF));
            ms.WriteByte((byte)((value >> 8) & 0xFF));
            ms.WriteByte((byte)(value & 0xFF));
        }

        private static uint ReadBigEndianUint(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) |
                   ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) |
                   (uint)data[offset + 3];
        }
    }
}
