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

using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace Mori.LocateAnything.Demo.Camera;

/// <summary>
/// Live-camera demo. Two loops run on background threads:
///  - capture: reads frames at camera rate, overlays the most recent detections,
///    and pushes the composited frame to the UI (video stays smooth);
///  - detection: repeatedly takes the newest frame and runs the model on it
///    (roughly 1 image/second on GPU), publishing boxes for the overlay.
/// The boxes therefore lag the video by one detection interval - the standard
/// pattern for putting a heavyweight detector behind a live stream.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ComboBox _cameraSelect;
    private readonly Button _refreshButton;
    private readonly TextBox _categories;
    private readonly Button _startStopButton;
    private readonly PictureBox _picture;
    private readonly Label _status;

    private LocateEngine? _engine;
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Thread? _captureThread;
    private Thread? _detectThread;

    // Shared state between the loops; guarded by _sync.
    private readonly object _sync = new();
    private Mat? _latestFrame;
    private IReadOnlyList<Detection> _latestDetections = [];

    // Volatile snapshot of the prompt so edits apply on the next detection.
    private volatile string _prompt = LocateEngine.PromptFor("person");

    public MainForm()
    {
        Text = "LocateAnything - Live Camera Demo";
        ClientSize = new System.Drawing.Size(1100, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 36, Padding = new Padding(4), WrapContents = false,
        };
        _cameraSelect = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        _refreshButton = new Button { Text = "Refresh", AutoSize = true };
        _refreshButton.Click += (_, _) => RefreshCameras();
        _categories = new TextBox { Width = 300, Text = "person" };
        _categories.TextChanged += (_, _) => UpdatePrompt();
        _startStopButton = new Button { Text = "Start", AutoSize = true };
        _startStopButton.Click += async (_, _) => await ToggleAsync();

        topBar.Controls.Add(new Label { Text = "Camera:", AutoSize = true, Margin = new Padding(0, 8, 2, 0) });
        topBar.Controls.Add(_cameraSelect);
        topBar.Controls.Add(_refreshButton);
        topBar.Controls.Add(new Label { Text = "Find:", AutoSize = true, Margin = new Padding(12, 8, 2, 0) });
        topBar.Controls.Add(_categories);
        topBar.Controls.Add(_startStopButton);

        _status = new Label { Dock = DockStyle.Bottom, Height = 22, Text = "Pick a camera and press Start." };
        _picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = System.Drawing.Color.Black };

        Controls.Add(_picture);
        Controls.Add(topBar);
        Controls.Add(_status);

        Shown += (_, _) => RefreshCameras();
    }

    private void UpdatePrompt()
    {
        string[] cats = _categories.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (cats.Length > 0) _prompt = LocateEngine.PromptFor(cats);
    }

    /// <summary>Probes for cameras off the UI thread (probing briefly opens each device).</summary>
    private async void RefreshCameras()
    {
        _refreshButton.Enabled = false;
        _status.Text = "Scanning for cameras...";
        var cameras = await Task.Run(CameraEnumerator.Discover);
        _cameraSelect.Items.Clear();
        foreach (var cam in cameras) _cameraSelect.Items.Add(cam);
        if (_cameraSelect.Items.Count > 0) _cameraSelect.SelectedIndex = 0;
        _status.Text = cameras.Count > 0 ? $"{cameras.Count} camera(s) found." : "No camera found.";
        _refreshButton.Enabled = true;
    }

    private async Task ToggleAsync()
    {
        if (_cts is not null) { Stop(); return; }
        if (_cameraSelect.SelectedItem is not CameraInfo cam) return;

        _startStopButton.Enabled = false;
        try
        {
            // One-time model load, kept across start/stop cycles.
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

            _capture = await Task.Run(() => new VideoCapture(cam.Index, VideoCaptureAPIs.DSHOW));
            if (!_capture.IsOpened())
            {
                _status.Text = $"Could not open {cam.Name}.";
                _capture.Dispose();
                _capture = null;
                return;
            }

            _cts = new CancellationTokenSource();
            _captureThread = new Thread(() => CaptureLoop(_cts.Token)) { IsBackground = true, Name = "camera-capture" };
            _detectThread = new Thread(() => DetectLoop(_cts.Token)) { IsBackground = true, Name = "camera-detect" };
            _captureThread.Start();
            _detectThread.Start();
            _startStopButton.Text = "Stop";
            _status.Text = $"Streaming {cam.Name} on {_engine.DeviceName}.";
        }
        finally
        {
            _startStopButton.Enabled = true;
        }
    }

    private void Stop()
    {
        _cts?.Cancel();
        _captureThread?.Join();
        _detectThread?.Join();
        _cts = null;
        _captureThread = _detectThread = null;
        _capture?.Dispose();
        _capture = null;
        lock (_sync) { _latestFrame?.Dispose(); _latestFrame = null; }
        _startStopButton.Text = "Start";
        _status.Text = "Stopped.";
    }

    /// <summary>Reads frames at camera rate and shows them with the current overlay.</summary>
    private void CaptureLoop(CancellationToken token)
    {
        using var frame = new Mat();
        while (!token.IsCancellationRequested && _capture is { } capture)
        {
            if (!capture.Read(frame) || frame.Empty()) { Thread.Sleep(10); continue; }

            // Publish a copy for the detection loop.
            lock (_sync)
            {
                _latestFrame?.Dispose();
                _latestFrame = frame.Clone();
            }

            // Composite the latest detections onto this frame and hand it to the UI.
            IReadOnlyList<Detection> boxes = _latestDetections;
            DrawDetections(frame, boxes);
            var bmp = frame.ToBitmap();
            try
            {
                BeginInvoke(() =>
                {
                    if (IsDisposed) { bmp.Dispose(); return; }
                    _picture.Image?.Dispose();
                    _picture.Image = bmp;
                });
            }
            catch (ObjectDisposedException) { bmp.Dispose(); break; }
        }
    }

    /// <summary>Continuously detects on the newest frame; the video never waits for it.</summary>
    private void DetectLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Mat? snapshot = null;
            lock (_sync) { if (_latestFrame is not null) snapshot = _latestFrame.Clone(); }
            if (snapshot is null) { Thread.Sleep(50); continue; }

            try
            {
                // JPEG round-trip: the native engine takes encoded image bytes.
                Cv2.ImEncode(".jpg", snapshot, out byte[] encoded);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var detections = _engine!.Locate(encoded, _prompt);
                sw.Stop();

                _latestDetections = detections;
                try
                {
                    BeginInvoke(() =>
                    {
                        if (!IsDisposed)
                            _status.Text = $"{detections.Count} detections | {sw.Elapsed.TotalMilliseconds:F0} ms/inference " +
                                           $"({1000.0 / sw.Elapsed.TotalMilliseconds:F1}/s) on {_engine.DeviceName}";
                    });
                }
                catch (ObjectDisposedException) { break; }
            }
            catch (Exception)
            {
                // Transient failure (e.g. camera unplugged mid-encode): drop this round.
                Thread.Sleep(200);
            }
            finally
            {
                snapshot.Dispose();
            }
        }
    }

    /// <summary>Draws boxes + label chips directly on the frame (BGR color space).</summary>
    private static void DrawDetections(Mat frame, IReadOnlyList<Detection> detections)
    {
        // Thickness scales with resolution so overlays stay readable at 1080p+.
        int thickness = Math.Max(2, frame.Height / 360);
        double fontScale = Math.Max(0.5, frame.Height / 720.0);

        foreach (var d in detections)
        {
            Scalar color = ColorFor(d.Label);
            var box = new Rect((int)d.X1, (int)d.Y1, (int)d.Width, (int)d.Height);
            Cv2.Rectangle(frame, box, color, thickness);

            var textSize = Cv2.GetTextSize(d.Label, HersheyFonts.HersheySimplex, fontScale, thickness, out int baseline);
            var textOrigin = new OpenCvSharp.Point(box.X, Math.Max(textSize.Height + 2, box.Y - 4));
            Cv2.Rectangle(frame,
                new Rect(textOrigin.X, textOrigin.Y - textSize.Height - 2, textSize.Width + 4, textSize.Height + baseline),
                color, -1);
            Cv2.PutText(frame, d.Label, new OpenCvSharp.Point(textOrigin.X + 2, textOrigin.Y),
                HersheyFonts.HersheySimplex, fontScale, Scalar.White, thickness);
        }
    }

    /// <summary>Stable, distinct BGR color per label.</summary>
    private static Scalar ColorFor(string label)
    {
        int h = Math.Abs(label.GetHashCode()) % 180;  // OpenCV hue range is 0-179
        using var hsv = new Mat(1, 1, MatType.CV_8UC3, new Scalar(h, 220, 220));
        using var bgr = new Mat();
        Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
        Vec3b c = bgr.Get<Vec3b>(0, 0);
        return new Scalar(c.Item0, c.Item1, c.Item2);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Stop();
        _engine?.Dispose();
        base.OnFormClosed(e);
    }
}
