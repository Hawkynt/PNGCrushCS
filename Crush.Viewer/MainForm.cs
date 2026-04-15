using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileFormat.Core;
using Optimizer.Image;

namespace Crush.Viewer;

internal sealed partial class MainForm : Form {

  private readonly ImagePanel _imagePanel;
  private readonly ThumbnailStrip _thumbnailStrip;
  private readonly StatusStrip _statusBar;
  private readonly ToolStripStatusLabel _formatLabel;
  private readonly ToolStripStatusLabel _dimensionsLabel;
  private readonly ToolStripStatusLabel _fileSizeLabel;
  private readonly ToolStripStatusLabel _zoomLabel;
  private readonly ToolStripStatusLabel _indexLabel;

  private ToolStripMenuItem _prevItem = null!;
  private ToolStripMenuItem _nextItem = null!;
  private ToolStripMenuItem _firstItem = null!;
  private ToolStripMenuItem _lastItem = null!;

  private FileInfo? _currentFile;
  private ImageFormat _currentFormat;
  private Bitmap? _currentBitmap;
  private RawImage? _currentRawImage;
  private string? _pendingFile;
  private CancellationTokenSource? _loadCts;

  private int _imageCount;
  private int _currentIndex;

  internal MainForm() {
    Text = "Crush Viewer";
    Size = new(1024, 768);
    StartPosition = FormStartPosition.CenterScreen;
    AllowDrop = true;
    KeyPreview = true;

    var menuStrip = _CreateMenuStrip();
    _statusBar = _CreateStatusBar(out _formatLabel, out _dimensionsLabel, out _fileSizeLabel, out _zoomLabel, out _indexLabel);
    _imagePanel = new ImagePanel { Dock = DockStyle.Fill };
    _thumbnailStrip = new ThumbnailStrip();
    _thumbnailStrip.IndexSelected += _NavigateToIndex;

    Controls.Add(_imagePanel);
    Controls.Add(_thumbnailStrip);
    Controls.Add(_statusBar);
    Controls.Add(menuStrip);
    MainMenuStrip = menuStrip;

    DragEnter += _OnDragEnter;
    DragDrop += _OnDragDrop;
    KeyDown += _OnKeyDown;
    _imagePanel.Paint += (_, _) => _UpdateZoomLabel();
  }

  internal void OpenFileOnLoad(string path) => _pendingFile = path;

  protected override void OnShown(EventArgs e) {
    base.OnShown(e);
    if (_pendingFile != null)
      _LoadFile(new FileInfo(_pendingFile));
  }

  private MenuStrip _CreateMenuStrip() {
    var menu = new MenuStrip();

    var file = new ToolStripMenuItem("&File");
    file.DropDownItems.Add(new ToolStripMenuItem("&Open...", null, (_, _) => _OpenFileDialog()) { ShortcutKeys = Keys.Control | Keys.O });
    file.DropDownItems.Add(new ToolStripMenuItem("Save &As...", null, (_, _) => _SaveAsDialog()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S });
    file.DropDownItems.Add(new ToolStripSeparator());
    file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()) { ShortcutKeys = Keys.Alt | Keys.F4 });
    menu.Items.Add(file);

    var view = new ToolStripMenuItem("&View");
    view.DropDownItems.Add(new ToolStripMenuItem("Zoom &In", null, (_, _) => _imagePanel.ZoomIn()) { ShortcutKeys = Keys.Control | Keys.Oemplus });
    view.DropDownItems.Add(new ToolStripMenuItem("Zoom &Out", null, (_, _) => _imagePanel.ZoomOut()) { ShortcutKeys = Keys.Control | Keys.OemMinus });
    view.DropDownItems.Add(new ToolStripMenuItem("&Fit to Window", null, (_, _) => _imagePanel.FitToWindow()) { ShortcutKeys = Keys.Control | Keys.D0 });
    view.DropDownItems.Add(new ToolStripMenuItem("&Actual Size (1:1)", null, (_, _) => _imagePanel.ActualSize()) { ShortcutKeys = Keys.Control | Keys.D1 });
    menu.Items.Add(view);

    var image = new ToolStripMenuItem("&Image");
    _firstItem = new ToolStripMenuItem("&First", null, (_, _) => _NavigateToIndex(0)) { ShortcutKeys = Keys.Control | Keys.Home, Enabled = false };
    _prevItem = new ToolStripMenuItem("&Previous", null, (_, _) => _NavigateImage(-1)) { ShortcutKeyDisplayString = "Left", Enabled = false };
    _nextItem = new ToolStripMenuItem("&Next", null, (_, _) => _NavigateImage(1)) { ShortcutKeyDisplayString = "Right", Enabled = false };
    _lastItem = new ToolStripMenuItem("&Last", null, (_, _) => _NavigateToIndex(_imageCount - 1)) { ShortcutKeys = Keys.Control | Keys.End, Enabled = false };
    image.DropDownItems.AddRange([_firstItem, _prevItem, _nextItem, _lastItem]);
    menu.Items.Add(image);

    var help = new ToolStripMenuItem("&Help");
    help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (_, _) => MessageBox.Show(
      "Crush Viewer\nImage viewer supporting 500+ formats\n\nPart of PNGCrushCS",
      "About Crush Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information)));
    menu.Items.Add(help);

    return menu;
  }

  private static StatusStrip _CreateStatusBar(
    out ToolStripStatusLabel format, out ToolStripStatusLabel dimensions,
    out ToolStripStatusLabel fileSize, out ToolStripStatusLabel zoom,
    out ToolStripStatusLabel index
  ) {
    var bar = new StatusStrip();
    format = new ToolStripStatusLabel("Ready") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    dimensions = new ToolStripStatusLabel("") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    fileSize = new ToolStripStatusLabel("") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    zoom = new ToolStripStatusLabel("") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    index = new ToolStripStatusLabel("") { Spring = false, AutoSize = true };
    var spacer = new ToolStripStatusLabel("") { Spring = true };
    bar.Items.AddRange([format, dimensions, fileSize, zoom, index, spacer]);
    return bar;
  }

  private void _OpenFileDialog() {
    using var dlg = new OpenFileDialog { Title = "Open Image", Filter = "All Files (*.*)|*.*" };
    if (dlg.ShowDialog() == DialogResult.OK)
      _LoadFile(new FileInfo(dlg.FileName));
  }

  private void _SaveAsDialog() {
    if (_currentRawImage == null) {
      MessageBox.Show("No image loaded.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    var targets = FormatRegistry.ConversionTargets.OrderBy(e => e.Name).ToList();
    var filters = targets.Select(e => $"{e.Name} (*{e.PrimaryExtension})|*{e.PrimaryExtension}").ToList();
    filters.Insert(0, "All Files (*.*)|*.*");

    using var dlg = new SaveFileDialog { Title = "Save Image As", Filter = string.Join("|", filters), FilterIndex = 1 };
    if (dlg.ShowDialog() != DialogResult.OK) return;

    try {
      var filterIndex = dlg.FilterIndex - 1;
      if (filterIndex <= 0 || filterIndex > targets.Count) {
        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        var entry = FormatRegistry.GetEntry(FormatRegistry.DetectFromExtension(ext));
        if (entry == null) { MessageBox.Show($"Cannot determine output format for '{ext}'.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        File.WriteAllBytes(dlg.FileName, entry.ConvertFromRawImage(_currentRawImage));
      } else {
        File.WriteAllBytes(dlg.FileName, targets[filterIndex - 1].ConvertFromRawImage(_currentRawImage));
      }
      _formatLabel.Text = "Saved";
    } catch (Exception ex) {
      MessageBox.Show($"Save failed: {ex.Message}", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private async void _LoadFile(FileInfo file) {
    _loadCts?.Cancel();
    _loadCts?.Dispose();
    _loadCts = new CancellationTokenSource();
    var ct = _loadCts.Token;

    _formatLabel.Text = $"Loading {file.Name}...";
    Enabled = false;
    try {
      var (format, rawImage, bitmap, imageCount) = await Task.Run(() => {
        var fmt = ImageFormatDetector.Detect(file);
        if (fmt == ImageFormat.Unknown) return (fmt, (RawImage?)null, (Bitmap?)null, 0);
        ct.ThrowIfCancellationRequested();
        var raw = BitmapConverter.LoadRawImage(file, fmt);
        ct.ThrowIfCancellationRequested();
        var bmp = raw != null ? BitmapConverter.RawImageToBitmap(raw) : BitmapConverter.LoadBitmap(file, fmt);
        var entry = FormatRegistry.GetEntry(fmt);
        var count = entry?.GetImageCount?.Invoke(file) ?? 0;
        if (count < 2) count = 0;
        return (fmt, raw, bmp, count);
      }, ct);

      if (ct.IsCancellationRequested) return;
      if (format == ImageFormat.Unknown) { MessageBox.Show($"Unknown image format: {file.Name}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
      if (rawImage == null && bitmap == null) { MessageBox.Show($"Format detected ({format}) but could not decode: {file.Name}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

      _currentFormat = format;
      _currentFile = file;
      _currentRawImage = rawImage;
      var oldBmp = _currentBitmap;
      _currentBitmap = bitmap;
      _imagePanel.Image = _currentBitmap;
      oldBmp?.Dispose();

      _imageCount = imageCount;
      _currentIndex = 0;

      _UpdateMultiImageUI();
      _UpdateStatusBar();
      Text = $"Crush Viewer - {file.Name}";
    } catch (OperationCanceledException) {
    } catch (Exception ex) {
      MessageBox.Show($"Failed to load: {ex.Message}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Error);
    } finally {
      Enabled = true;
    }
  }

  private void _NavigateImage(int delta) {
    if (_imageCount < 2 || _currentFile == null) return;
    _NavigateToIndex(Math.Clamp(_currentIndex + delta, 0, _imageCount - 1));
  }

  private void _NavigateToIndex(int index) {
    if (_imageCount < 2 || _currentFile == null) return;
    if (index < 0 || index >= _imageCount || index == _currentIndex) return;

    _currentIndex = index;
    var entry = FormatRegistry.GetEntry(_currentFormat);
    var raw = entry?.LoadRawImageAtIndex?.Invoke(_currentFile, _currentIndex);
    if (raw == null) return;

    _currentRawImage = raw;
    var oldBmp = _currentBitmap;
    _currentBitmap = BitmapConverter.RawImageToBitmap(raw);
    _imagePanel.Image = _currentBitmap;
    oldBmp?.Dispose();

    _thumbnailStrip.Select(_currentIndex);
    _UpdateStatusBar();
    _UpdateNavigationState();
  }

  private void _UpdateMultiImageUI() {
    _UpdateNavigationState();

    if (_imageCount >= 2) {
      var entry = FormatRegistry.GetEntry(_currentFormat);
      var file = _currentFile!;
      _thumbnailStrip.SetSource(_imageCount, async (i, ct) => await Task.Run(() => {
        try {
          var raw = entry?.LoadRawImageAtIndex?.Invoke(file, i);
          if (raw == null) return null;
          using var bmp = BitmapConverter.RawImageToBitmap(raw);
          var scale = Math.Min(64f / bmp.Width, 64f / bmp.Height);
          var thumb = new Bitmap(Math.Max(1, (int)(bmp.Width * scale)), Math.Max(1, (int)(bmp.Height * scale)));
          using (var g = Graphics.FromImage(thumb)) {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(bmp, 0, 0, thumb.Width, thumb.Height);
          }
          return thumb;
        } catch { return null; }
      }, ct));
    } else {
      _thumbnailStrip.Clear();
    }
  }

  private void _UpdateNavigationState() {
    var m = _imageCount >= 2;
    _firstItem.Enabled = m && _currentIndex > 0;
    _prevItem.Enabled = m && _currentIndex > 0;
    _nextItem.Enabled = m && _currentIndex < _imageCount - 1;
    _lastItem.Enabled = m && _currentIndex < _imageCount - 1;
  }

  private void _UpdateStatusBar() {
    _formatLabel.Text = _currentFormat.ToString();
    if (_currentBitmap != null) _dimensionsLabel.Text = $"{_currentBitmap.Width} x {_currentBitmap.Height}";
    if (_currentFile != null) _fileSizeLabel.Text = _FormatSize(_currentFile.Length);
    _UpdateZoomLabel();
    _indexLabel.Text = _imageCount > 1 ? $"{_currentIndex + 1}/{_imageCount}" : "";
  }

  private void _UpdateZoomLabel() => _zoomLabel.Text = $"{_imagePanel.Zoom * 100:F0}%";

  private static string _FormatSize(long bytes) => bytes switch {
    < 1024 => $"{bytes} B",
    < 1048576 => $"{bytes / 1024.0:F1} KiB",
    < 1073741824 => $"{bytes / 1048576.0:F1} MiB",
    _ => $"{bytes / 1073741824.0:F2} GiB",
  };

  private void _OnDragEnter(object? sender, DragEventArgs e) {
    if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
  }

  private void _OnDragDrop(object? sender, DragEventArgs e) {
    if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
      _LoadFile(new FileInfo(files[0]));
  }

  private void _OnKeyDown(object? sender, KeyEventArgs e) {
    switch (e.KeyCode) {
      case Keys.Oemplus or Keys.Add: _imagePanel.ZoomIn(); e.Handled = true; break;
      case Keys.OemMinus or Keys.Subtract: _imagePanel.ZoomOut(); e.Handled = true; break;
      case Keys.Left: _NavigateImage(-1); e.Handled = true; break;
      case Keys.Right: _NavigateImage(1); e.Handled = true; break;
      case Keys.PageUp: _NavigateImage(-10); e.Handled = true; break;
      case Keys.PageDown: _NavigateImage(10); e.Handled = true; break;
      case Keys.Home: _NavigateToIndex(0); e.Handled = true; break;
      case Keys.End: _NavigateToIndex(_imageCount - 1); e.Handled = true; break;
    }
  }

  protected override void Dispose(bool disposing) {
    if (disposing) {
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _loadCts = null;
      _thumbnailStrip.Clear();
      _currentBitmap?.Dispose();
      _currentBitmap = null;
    }
    base.Dispose(disposing);
  }
}
