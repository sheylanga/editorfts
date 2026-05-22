using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace edf
{
    public partial class Form1 : Form
    {
        private Bitmap? loadedBitmap;
        private Bitmap? workingBitmap;
        private bool isDrawing = false;
        private bool isCropping = false;
        private Point lastPoint;
        private Point cropStart;
        private Rectangle cropRect = Rectangle.Empty;
        private Color brushColor = Color.Black;
        private int brushSize = 5;
        private double lat;
        private double lon;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            loadedBitmap?.Dispose();
            workingBitmap?.Dispose();
        }

        private void btnRevert_Click(object? sender, EventArgs e)
        {
            if (loadedBitmap == null) return;
            // Restore baseline
            ReplaceBitmap((Bitmap)loadedBitmap.Clone());
            // Reset sliders
            trackBrightness.Value = 0;
            trackContrast.Value = 0;
            trackSaturation.Value = 0;
            trackBrushSize.Value = 5;
            brushSize = 5;
            lblBrushSizeValue.Text = brushSize.ToString();
        }

        private void btnOpen_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog();
            ofd.Filter = "All Image Files|*.bmp;*.dib;*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.gif;*.tif;*.tiff;*.ico|All Files|*.*";

            if (ofd.ShowDialog() != DialogResult.OK) return;

            try
            {
                // 1. Cargamos la imagen (Tu parte original)
                var loaded = new Bitmap(ofd.FileName);
                loaded.Tag = ofd.FileName; // Guardar la ruta para MetadataExtractor
                LoadBitmap(loaded); // Esto actualiza el canvas y el workingBitmap

                // 2. Implementación de GPS (Lo que quieres agregar)
                // Usamos 'loaded' que ya es la imagen abierta
                var coordenadas = ObtenerCoordenadas(loaded);

                if (coordenadas != null)
                {
                    txtCoordenadas.Text = $"{coordenadas.Value.lat}, {coordenadas.Value.lon}";

                    // Actualizamos variables globales y mostramos mapa
                    lat = coordenadas.Value.lat;
                    lon = coordenadas.Value.lon;
                    MostrarMapa(lat, lon);
                }
                else
                {
                    txtCoordenadas.Text = "No se encontraron datos GPS.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir el archivo como imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void MostrarMapa(double lat, double lon)
        {
            await webMapa.EnsureCoreWebView2Async();

            string html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
</head>
<body style='margin:0'>
    <iframe
        width='100%'
        height='100%'
        frameborder='0'
        style='border:0'
        src='https://www.google.com/maps?q={lat},{lon}&z=10&output=embed'>
    </iframe>
</body>
</html>";

            webMapa.NavigateToString(html);
        }

        private (double lat, double lon)? ObtenerCoordenadas(Image img)
        {
            try
            {
                // Obtener la ruta del archivo desde la imagen
                string? imagePath = img.Tag as string;
                if (string.IsNullOrEmpty(imagePath))
                    return null;

                // Extraer metadatos EXIF usando MetadataExtractor
                var directories = ImageMetadataReader.ReadMetadata(imagePath);
                var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

                if (gpsDirectory == null)
                    return null;

                var location = gpsDirectory.GetGeoLocation();
                if (location == null)
                    return null;

                lat = location.Latitude;
                lon = location.Longitude;

                return (lat, lon);
            }
            catch
            {
                return null;
            }
        }

        private string GetString(PropertyItem prop)
        {
            return System.Text.Encoding.ASCII.GetString(prop.Value).Trim('\0');
        }

        private void btnMostrarUbicacion_Click(object sender, EventArgs e)
        {
            // Primero intenta usar las variables globales lat/lon
            if (lat != 0 && lon != 0)
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"https://www.google.com/maps?q={lat},{lon}",
                        UseShellExecute = true
                    });
                return;
            }

            // Si no hay coordenadas globales, intenta parsear del txtCoordenadas
            if (TryParseCoordinates(txtCoordenadas.Text, out double latitude, out double longitude))
            {
                lat = latitude;
                lon = longitude;
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"https://www.google.com/maps?q={lat},{lon}",
                        UseShellExecute = true
                    });
            }
            else
            {
                MessageBox.Show("Por favor ingresa coordenadas válidas primero (ej: 26.79692, 101.42861)", "Coordenadas inválidas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnVer_Click(object? sender, EventArgs e)
        {
            if (loadedBitmap == null)
            {
                txtCoordenadas.Text = "No hay imagen cargada.";
                return;
            }

            // Primero intenta obtener coordenadas del EXIF
            var coordenadas = ObtenerCoordenadas(loadedBitmap);

            if (coordenadas != null)
            {
                lat = coordenadas.Value.lat;
                lon = coordenadas.Value.lon;
                txtCoordenadas.Text = $"{coordenadas.Value.lat}, {coordenadas.Value.lon}";
                MostrarMapa(lat, lon);
            }
            else
            {
                // Si no hay GPS, intenta parsear coordenadas ingresadas manualmente
                if (TryParseCoordinates(txtCoordenadas.Text, out double latitude, out double longitude))
                {
                    lat = latitude;
                    lon = longitude;
                    MostrarMapa(lat, lon);
                }
                else
                {
                    txtCoordenadas.Text = "No se encontraron datos GPS. Ingresa coordenadas manualmente (ej: 26.79692, 101.42861)";
                }
            }
        }

        private bool TryParseCoordinates(string input, out double latitude, out double longitude)
        {
            latitude = 0;
            longitude = 0;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Intenta parsear formato con símbolos (ej: "26.79692° N, 101.42861° O")
            if (TryParseWithSymbols(input, out latitude, out longitude))
                return true;

            // Intenta parsear formato simple (ej: "26.79692, 101.42861")
            var parts = input.Split(',');
            if (parts.Length != 2)
                return false;

            if (double.TryParse(parts[0].Trim(), out latitude) &&
                double.TryParse(parts[1].Trim(), out longitude))
            {
                // Validar que sean coordenadas válidas
                if (latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180)
                    return true;
            }

            return false;
        }

        private bool TryParseWithSymbols(string input, out double latitude, out double longitude)
        {
            latitude = 0;
            longitude = 0;

            try
            {
                // Soporta formatos como: "26.79692° N, 101.42861° O" o "26.79692°N, 101.42861°O"
                var parts = input.Split(',');
                if (parts.Length != 2)
                    return false;

                var latPart = parts[0].Trim();
                var lonPart = parts[1].Trim();

                // Extraer latitud
                if (!ExtractCoordinate(latPart, out latitude, out string latDir))
                    return false;

                // Extraer longitud
                if (!ExtractCoordinate(lonPart, out longitude, out string lonDir))
                    return false;

                // Aplicar signos según dirección
                if (latDir.Equals("S", StringComparison.OrdinalIgnoreCase))
                    latitude = -latitude;

                if (lonDir.Equals("W", StringComparison.OrdinalIgnoreCase) || 
                    lonDir.Equals("O", StringComparison.OrdinalIgnoreCase))
                    longitude = -longitude;

                // Validar rangos
                if (latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool ExtractCoordinate(string input, out double value, out string direction)
        {
            value = 0;
            direction = "";

            // Remover espacios
            input = input.Replace(" ", "").ToUpper();

            // Buscar caracteres especiales y dirección
            char[] separators = { '°', '°', '°' }; // diferentes tipos de grado
            var parts = input.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 1)
                return false;

            // El primer parte debe ser el número
            if (!double.TryParse(parts[0], out value))
                return false;

            // La segunda parte (si existe) es la dirección
            if (parts.Length > 1)
            {
                direction = parts[1];
                if (direction.Length > 0)
                    direction = direction[0].ToString();
            }

            // Si no hay dirección explícita, asumimos la predeterminada
            if (string.IsNullOrEmpty(direction))
            {
                direction = "N"; // Latitud predeterminada es Norte
            }

            return true;
        }

        private void LoadBitmap(Bitmap bmp)
        {
            // load baseline image (used as source for adjustments)
            loadedBitmap?.Dispose();
            workingBitmap?.Dispose();

            loadedBitmap = bmp;
            workingBitmap = (Bitmap)loadedBitmap.Clone();
            canvas.Image = workingBitmap;

            // reset sliders
            trackBrightness.Value = 0;
            trackContrast.Value = 0;
            trackSaturation.Value = 0;

            // set brush size label
            try { lblBrushSizeValue.Text = trackBrushSize.Value.ToString(); } catch { }
        }

        private void ReplaceBitmap(Bitmap newBmp)
        {
            // Replace only working bitmap (do not overwrite loaded baseline)
            workingBitmap?.Dispose();
            workingBitmap = newBmp;
            canvas.Image = workingBitmap;
        }

        private void btnSave_Click(object? sender, EventArgs e)
        {
            if (workingBitmap == null) return;
            using var sfd = new SaveFileDialog();
            sfd.Filter = "PNG|*.png|JPEG|*.jpg";
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var fmt = sfd.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
            workingBitmap.Save(sfd.FileName, fmt);
        }

        private void btnGray_Click(object? sender, EventArgs e)
        {
            ApplyProcessor(p => ImageProcessor.ApplyGrayscale(p));
        }

        private void btnSepia_Click(object? sender, EventArgs e)
        {
            ApplyProcessor(p => ImageProcessor.ApplySepia(p));
        }

        private void btnInvert_Click(object? sender, EventArgs e)
        {
            ApplyProcessor(p => ImageProcessor.InvertColors(p));
        }

        private void btnCrop_Click(object? sender, EventArgs e)
        {
            isCropping = !isCropping;
            btnCrop.Text = isCropping ? "Recortando... (Click y arrastra)" : "Recortar";
            if (!isCropping) cropRect = Rectangle.Empty;
        }

        private void btnBrush_Click(object? sender, EventArgs e)
        {
            isDrawing = !isDrawing;
            btnBrush.Text = isDrawing ? "Pintando..." : "Pincel";
        }

        private void btnColor_Click(object? sender, EventArgs e)
        {
            using var cd = new ColorDialog();
            if (cd.ShowDialog() != DialogResult.OK) return;
            brushColor = cd.Color;
            panelColorPreview.BackColor = brushColor;
        }

        private void trackBrushSize_Scroll(object? sender, EventArgs e)
        {
            brushSize = trackBrushSize.Value;
            lblBrushSizeValue.Text = brushSize.ToString();
        }

        private void TrackBar_Scroll(object? sender, EventArgs e)
        {
            var source = loadedBitmap ?? workingBitmap;
            if (source == null) return;

            // Apply brightness, contrast, saturation using ColorMatrix via ImageProcessor
            var b = trackBrightness.Value; // -100..100
            var c = trackContrast.Value; // -100..100
            var s = trackSaturation.Value; // -100..100

            workingBitmap?.Dispose();
            workingBitmap = ImageProcessor.Adjust(source, b, c, s);
            canvas.Image = workingBitmap;
        }

        private void ApplyProcessor(Func<Bitmap, Bitmap> op)
        {
            if (workingBitmap == null) return;
            using var tmp = (Bitmap)workingBitmap.Clone();
            var result = op(tmp);
            ReplaceBitmap(result);
        }

        private void canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (workingBitmap == null) return;

            lastPoint = e.Location;
            if (isDrawing && e.Button == MouseButtons.Left)
            {
                DrawAt(e.Location);
            }
            else if (isCropping && e.Button == MouseButtons.Left)
            {
                cropStart = e.Location;
                cropRect = new Rectangle(e.Location, new Size(0, 0));
            }
        }

        private void canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (workingBitmap == null) return;

            if (isDrawing && e.Button == MouseButtons.Left)
            {
                using var g = Graphics.FromImage(workingBitmap);
                using var p = new Pen(brushColor, brushSize) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawLine(p, lastPoint, e.Location);
                lastPoint = e.Location;
                canvas.Invalidate();
            }
            else if (isCropping && e.Button == MouseButtons.Left)
            {
                var x = Math.Min(cropStart.X, e.X);
                var y = Math.Min(cropStart.Y, e.Y);
                var w = Math.Abs(cropStart.X - e.X);
                var h = Math.Abs(cropStart.Y - e.Y);
                cropRect = new Rectangle(x, y, w, h);
                canvas.Invalidate();
            }
        }

        private void canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            if (workingBitmap == null) return;

            if (isCropping && cropRect.Width > 0 && cropRect.Height > 0)
            {
                // Map control coordinates to image coordinates
                var imgRect = GetImageDisplayRectangle(workingBitmap, canvas);
                if (imgRect.Contains(cropRect))
                {
                    var scaleX = (float)workingBitmap.Width / imgRect.Width;
                    var scaleY = (float)workingBitmap.Height / imgRect.Height;
                    var rx = (int)((cropRect.X - imgRect.X) * scaleX);
                    var ry = (int)((cropRect.Y - imgRect.Y) * scaleY);
                    var rw = (int)(cropRect.Width * scaleX);
                    var rh = (int)(cropRect.Height * scaleY);
                    var crc = new Rectangle(rx, ry, rw, rh);
                    crc.Intersect(new Rectangle(0, 0, workingBitmap.Width, workingBitmap.Height));
                    if (crc.Width > 0 && crc.Height > 0)
                    {
                        using var cropped = workingBitmap.Clone(crc, workingBitmap.PixelFormat);
                        ReplaceBitmap(new Bitmap(cropped));
                    }
                }

                cropRect = Rectangle.Empty;
                canvas.Invalidate();
            }
        }

        private void DrawAt(Point p)
        {
            if (workingBitmap == null) return;
            using var g = Graphics.FromImage(workingBitmap);
            using var b = new SolidBrush(brushColor);
            var imgRect = GetImageDisplayRectangle(workingBitmap, canvas);
            if (!imgRect.Contains(p)) return;

            var scaleX = (float)workingBitmap.Width / imgRect.Width;
            var scaleY = (float)workingBitmap.Height / imgRect.Height;
            var rx = (int)((p.X - imgRect.X) * scaleX);
            var ry = (int)((p.Y - imgRect.Y) * scaleY);
            g.FillEllipse(b, rx - brushSize, ry - brushSize, brushSize * 2, brushSize * 2);
            canvas.Invalidate();
        }

        private Rectangle GetImageDisplayRectangle(Image img, PictureBox pic)
        {
            // Calculate where the image is drawn inside the PictureBox when SizeMode=Zoom
            var imgRatio = (double)img.Width / img.Height;
            var boxRatio = (double)pic.Width / pic.Height;
            int width, height, x, y;
            if (imgRatio > boxRatio)
            {
                width = pic.Width;
                height = (int)(pic.Width / imgRatio);
                x = 0;
                y = (pic.Height - height) / 2;
            }
            else
            {
                height = pic.Height;
                width = (int)(pic.Height * imgRatio);
                y = 0;
                x = (pic.Width - width) / 2;
            }
            return new Rectangle(x, y, width, height);
        }

        private void canvas_Paint(object? sender, PaintEventArgs e)
        {
            if (cropRect != Rectangle.Empty)
            {
                using var pen = new Pen(Color.Red) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                e.Graphics.DrawRectangle(pen, cropRect);
            }
        }

        private void lblContrast_Click(object sender, EventArgs e)
        {

        }

        private void tableLayoutPanel_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
