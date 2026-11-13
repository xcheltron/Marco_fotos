using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace Marco_fotos
{
    public partial class Form1 : Form
    {
        // ---- Ajustes ----
        private const int IntervaloMs = 5000;        // tiempo entre fotos
        private const int AnchoMarco = 300;          // tamaño FIJO del marco (px)
        private const int AltoMarco = 300;
        private const double OpacidadInicial = 0.75; // 1 = opaco, 0.2 = muy transparente
        private static readonly Color ColorFondo = Color.Black; // color de las barras si la foto no llena el marco

        private static readonly string[] Extensiones = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

        private readonly Timer timer = new Timer();
        private List<string> archivos = new List<string>();
        private int indice = -1;

        // Para arrastrar la ventana sin borde
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                FormBorderStyle = FormBorderStyle.None;
                TopMost = true;
                Opacity = OpacidadInicial;
                BackColor = ColorFondo;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(AnchoMarco, AltoMarco); // el tamaño se fija una sola vez
                DoubleBuffered = true;
                KeyPreview = true;
                BackgroundImageLayout = ImageLayout.None;     // la foto ya viene del tamaño exacto

                MouseDown += (s, ev) =>
                {
                    if (ev.Button == MouseButtons.Left)
                    {
                        ReleaseCapture();
                        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                    }
                };

                var menu = new ContextMenuStrip();
                menu.Items.Add("Anterior", null, (s, ev) => { Anterior(); ReiniciarTimer(); });
                menu.Items.Add("Siguiente", null, (s, ev) => { Siguiente(); ReiniciarTimer(); });
                menu.Items.Add(new ToolStripSeparator());

                var itemInicio = new ToolStripMenuItem("Iniciar con Windows") { Checked = IniciaConWindows() };
                itemInicio.Click += (s, ev) =>
                {
                    itemInicio.Checked = !itemInicio.Checked;
                    ConfigurarInicio(itemInicio.Checked);
                };
                menu.Items.Add(itemInicio);

                menu.Items.Add("Salir", null, (s, ev) => Close());
                ContextMenuStrip = menu;

                KeyDown += Form1_KeyDown;

                string carpeta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "imagenes");
                Directory.CreateDirectory(carpeta);

                archivos = Directory.GetFiles(carpeta)
                    .Where(f => Extensiones.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList();

                if (archivos.Count == 0)
                {
                    MessageBox.Show("No hay imágenes en:\n" + carpeta, "Marco de fotos");
                    return;
                }

                timer.Interval = IntervaloMs;
                timer.Tick += (s, ev) => Siguiente();
                timer.Start();

                Siguiente();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al iniciar:\n" + ex, "Marco de fotos");
            }
        }

        private void Siguiente() { MostrarImagen(indice + 1, 1); }
        private void Anterior() { MostrarImagen(indice - 1, -1); }
        private void ReiniciarTimer() { timer.Stop(); timer.Start(); }

        // ---- Iniciar con Windows (registro del usuario actual, no requiere administrador) ----
        private const string NombreApp = "MarcoFotos";
        private const string RutaRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static bool IniciaConWindows()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RutaRun))
            {
                return k != null && k.GetValue(NombreApp) != null;
            }
        }

        private static void ConfigurarInicio(bool activar)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RutaRun, true))
            {
                if (k == null) return;
                if (activar)
                    k.SetValue(NombreApp, "\"" + Application.ExecutablePath + "\"");
                else
                    k.DeleteValue(NombreApp, false);
            }
        }

        // Crea un bitmap del tamaño fijo del marco y dibuja la foto centrada,
        // conservando su proporción (sin deformarla).
        private Bitmap CargarEscalada(string ruta)
        {
            using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var original = Image.FromStream(fs, false, false))
            {
                double escala = Math.Min((double)AnchoMarco / original.Width,
                                         (double)AltoMarco / original.Height);
                int w = Math.Max(1, (int)Math.Round(original.Width * escala));
                int h = Math.Max(1, (int)Math.Round(original.Height * escala));
                int x = (AnchoMarco - w) / 2;
                int y = (AltoMarco - h) / 2;

                var destino = new Bitmap(AnchoMarco, AltoMarco, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(destino))
                {
                    g.Clear(ColorFondo);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(original, new Rectangle(x, y, w, h));
                }
                return destino;
            }
        }

        private void MostrarImagen(int nuevoIndice, int paso)
        {
            if (archivos.Count == 0) return;

            string ultimoArchivo = null;
            Exception ultimoError = null;

            for (int intento = 0; intento < archivos.Count; intento++)
            {
                int i = ((nuevoIndice % archivos.Count) + archivos.Count) % archivos.Count;
                try
                {
                    Bitmap nueva = CargarEscalada(archivos[i]);

                    var anterior = BackgroundImage;
                    BackgroundImage = nueva;
                    anterior?.Dispose();
                    Invalidate();

                    indice = i;
                    return;
                }
                catch (Exception ex)
                {
                    ultimoArchivo = archivos[i];
                    ultimoError = ex;
                    nuevoIndice += paso;
                }
            }

            timer.Stop();
            MessageBox.Show(
                "No se pudo abrir ninguna imagen.\n\nÚltimo archivo: " + ultimoArchivo +
                "\nError: " + ultimoError?.Message,
                "Marco de fotos");
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Right: Siguiente(); ReiniciarTimer(); break;
                case Keys.Left: Anterior(); ReiniciarTimer(); break;
                case Keys.Up: Opacity = Math.Min(1.0, Opacity + 0.1); break;
                case Keys.Down: Opacity = Math.Max(0.2, Opacity - 0.1); break;
                case Keys.Escape: Close(); break;
            }
        }
    }
}