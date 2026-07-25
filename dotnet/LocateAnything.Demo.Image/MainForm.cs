// Copyright 2026 Cristian Mori
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Drawing.Drawing2D;
using GdiImage = System.Drawing.Image;

namespace Mori.LocateAnything.Demo.Image;

/// <summary>
/// Single-window demo: pick a local image, type comma-separated categories,
/// press Detect, and the labeled boxes are drawn over the image. The model is
/// loaded lazily on first detection (it takes a few seconds) and kept alive
/// for the lifetime of the window.
/// </summary>
public sealed class MainForm : Form
{
    private readonly PictureBox _picture;
    private readonly TextBox _categories;
    private readonly Button _openButton;
    private readonly Button _detectButton;
    private readonly Label _status;

    private string? _imagePath;
    private LocateEngine? _engine;
    private bool _busy;

    public MainForm()
    {
        Text = "LocateAnything - Image Demo";
        ClientSize = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterScreen;

        // Top bar: [Open Image] [categories...........] [Detect]
        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 36, Padding = new Padding(4), WrapContents = false,
        };
        _openButton = new Button { Text = "Open Image...", AutoSize = true };
        _openButton.Click += (_, _) => OpenImage();
        _categories = new TextBox { Width = 400, Text = "person, car" };
        _detectButton = new Button { Text = "Detect", AutoSize = true, Enabled = false };
        _detectButton.Click += async (_, _) => await DetectAsync();
        topBar.Controls.Add(_openButton);
        topBar.Controls.Add(new Label { Text = "Find:", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        topBar.Controls.Add(_categories);
        topBar.Controls.Add(_detectButton);

        _status = new Label { Dock = DockStyle.Bottom, Height = 22, Text = "Open an image to begin." };
        _picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };

        Controls.Add(_picture);
        Controls.Add(topBar);
        Controls.Add(_status);
    }

    private void OpenImage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _imagePath = dialog.FileName;
        _picture.Image?.Dispose();
        _picture.Image = LoadBitmapWithoutLock(_imagePath);
        _detectButton.Enabled = true;
        _status.Text = $"Loaded {Path.GetFileName(_imagePath)} - press Detect.";
    }

    /// <summary>Runs one detection on a worker thread and redraws the image with boxes.</summary>
    private async Task DetectAsync()
    {
        if (_busy || _imagePath is null) return;
        _busy = true;
        _detectButton.Enabled = false;
        try
        {
            if (_engine is null)
            {
                string? model = ModelLocator.FindDefault();
                if (model is null)
                {
                    MessageBox.Show(this,
                        $"No GGUF model found. Set the {ModelLocator.EnvVar} environment variable " +
                        "or place a model in a 'models' folder next to the app.",
                        "Model not found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _status.Text = $"Loading {Path.GetFileName(model)} (one-time)...";
                _engine = await Task.Run(() => LocateEngine.Load(model));
            }

            string[] cats = _categories.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cats.Length == 0) { _status.Text = "Type at least one category."; return; }
            string prompt = LocateEngine.PromptFor(cats);
            string path = _imagePath;

            _status.Text = $"Detecting on {_engine.DeviceName} ...";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var detections = await Task.Run(() => _engine.Locate(path, prompt));
            sw.Stop();

            var annotated = DrawDetections(path, detections);
            _picture.Image?.Dispose();
            _picture.Image = annotated;
            _status.Text = $"{detections.Count} detections in {sw.Elapsed.TotalSeconds:F1} s on {_engine.DeviceName}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _busy = false;
            _detectButton.Enabled = _imagePath is not null;
        }
    }

    /// <summary>Returns a copy of the image with boxes + labels rendered on top.</summary>
    private static Bitmap DrawDetections(string imagePath, IReadOnlyList<Detection> detections)
    {
        var bmp = LoadBitmapWithoutLock(imagePath);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Pen/font thickness scales with image size so boxes stay visible on large photos.
        float scale = Math.Max(1f, Math.Min(bmp.Width, bmp.Height) / 500f);
        using var font = new Font(FontFamily.GenericSansSerif, 9f * scale, FontStyle.Bold);

        foreach (var d in detections)
        {
            Color color = ColorFor(d.Label);
            using var pen = new Pen(color, 2f * scale);
            g.DrawRectangle(pen, d.X1, d.Y1, d.Width, d.Height);

            SizeF textSize = g.MeasureString(d.Label, font);
            var textRect = new RectangleF(d.X1, Math.Max(0, d.Y1 - textSize.Height), textSize.Width, textSize.Height);
            using var back = new SolidBrush(Color.FromArgb(200, color));
            g.FillRectangle(back, textRect);
            g.DrawString(d.Label, font, Brushes.White, textRect.Location);
        }
        return bmp;
    }

    /// <summary>Stable, distinct color per label so multiple categories are tellable apart.</summary>
    private static Color ColorFor(string label)
    {
        int h = Math.Abs(label.GetHashCode()) % 360;
        return ColorFromHsv(h, 0.85, 0.85);
    }

    private static Color ColorFromHsv(double h, double s, double v)
    {
        int i = (int)(h / 60) % 6;
        double f = h / 60 - Math.Floor(h / 60);
        int p = (int)(255 * v * (1 - s)), q = (int)(255 * v * (1 - f * s)),
            t = (int)(255 * v * (1 - (1 - f) * s)), val = (int)(255 * v);
        return i switch
        {
            0 => Color.FromArgb(val, t, p), 1 => Color.FromArgb(q, val, p),
            2 => Color.FromArgb(p, val, t), 3 => Color.FromArgb(p, q, val),
            4 => Color.FromArgb(t, p, val), _ => Color.FromArgb(val, p, q),
        };
    }

    /// <summary>Loads a bitmap without keeping the file handle open (unlike Image.FromFile).</summary>
    private static Bitmap LoadBitmapWithoutLock(string path)
    {
        using var src = GdiImage.FromStream(new MemoryStream(File.ReadAllBytes(path)));
        return new Bitmap(src);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _engine?.Dispose();
        base.OnFormClosed(e);
    }
}
