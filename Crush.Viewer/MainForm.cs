using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileFormat.Core;
using Optimizer.Image;

namespace Crush.Viewer;

internal sealed partial class MainForm : Form {

  private readonly ImagePanel _imagePanel;
  private readonly StatusStrip _statusBar;
  private readonly ToolStripStatusLabel _formatLabel;
  private readonly ToolStripStatusLabel _dimensionsLabel;
  private readonly ToolStripStatusLabel _fileSizeLabel;
  private readonly ToolStripStatusLabel _zoomLabel;
  private readonly ToolStripStatusLabel _indexLabel;

  private FileInfo? _currentFile;
  private ImageFormat _currentFormat;
  private Bitmap? _currentBitmap;
  private RawImage? _currentRawImage;
  private string? _pendingFile;

  // Multi-image support
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

    Controls.Add(_imagePanel);
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
    image.DropDownItems.Add(new ToolStripMenuItem("&Previous", null, (_, _) => _NavigateImage(-1)) { ShortcutKeyDisplayString = "Left" });
    image.DropDownItems.Add(new ToolStripMenuItem("&Next", null, (_, _) => _NavigateImage(1)) { ShortcutKeyDisplayString = "Right" });
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
    using var dlg = new OpenFileDialog {
      Title = "Open Image",
      Filter = "All Files (*.*)|*.*",
    };
    if (dlg.ShowDialog() == DialogResult.OK)
      _LoadFile(new FileInfo(dlg.FileName));
  }

  private void _SaveAsDialog() {
    if (_currentRawImage == null) {
      MessageBox.Show("No image loaded.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    // Build filter from writable formats
    var targets = FormatRegistry.ConversionTargets.OrderBy(e => e.Name).ToList();
    var filters = targets.Select(e => $"{e.Name} (*{e.PrimaryExtension})|*{e.PrimaryExtension}").ToList();
    filters.Insert(0, "All Files (*.*)|*.*");

    using var dlg = new SaveFileDialog {
      Title = "Save Image As",
      Filter = string.Join("|", filters),
      FilterIndex = 1,
    };
    if (dlg.ShowDialog() != DialogResult.OK)
      return;

    try {
      // Determine target format from selected filter
      var filterIndex = dlg.FilterIndex - 1; // 0-based
      if (filterIndex <= 0 || filterIndex > targets.Count) {
        // "All Files" or out of range — detect from extension
        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        var detected = FormatRegistry.DetectFromExtension(ext);
        var entry = FormatRegistry.GetEntry(detected);
        if (entry == null) {
          MessageBox.Show($"Cannot determine output format for extension '{ext}'.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }
        var bytes = entry.ConvertFromRawImage(_currentRawImage);
        File.WriteAllBytes(dlg.FileName, bytes);
      } else {
        var entry = targets[filterIndex - 1];
        var bytes = entry.ConvertFromRawImage(_currentRawImage);
        File.WriteAllBytes(dlg.FileName, bytes);
      }

      _formatLabel.Text = "Saved";
    } catch (Exception ex) {
      MessageBox.Show($"Save failed: {ex.Message}", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private async void _LoadFile(FileInfo file) {
    _formatLabel.Text = "Loading...";
    Enabled = false;
    try {
      var (format, rawImage, bitmap, imageCount) = await Task.Run(() => {
        var fmt = ImageFormatDetector.Detect(file);
        if (fmt == ImageFormat.Unknown)
          return (fmt, (RawImage?)null, (Bitmap?)null, 0);

        var raw = BitmapConverter.LoadRawImage(file, fmt);
        var bmp = raw != null ? BitmapConverter.RawImageToBitmap(raw) : BitmapConverter.LoadBitmap(file, fmt);
        var entry = FormatRegistry.GetEntry(fmt);
        var count = entry?.GetImageCount?.Invoke(file) ?? 0;
        if (count < 2)
          count = 0;

        return (fmt, raw, bmp, count);
      });

      if (format == ImageFormat.Unknown) {
        MessageBox.Show($"Unknown image format: {file.Name}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }

      _currentFormat = format;
      _currentFile = file;
      _currentRawImage = rawImage;
      var oldBmp = _currentBitmap;
      _currentBitmap = bitmap;

      _imagePanel.Image = _currentBitmap;
      _imagePanel.FitToWindow();
      oldBmp?.Dispose();

      _imageCount = imageCount;
      _currentIndex = 0;

      _UpdateStatusBar();
      Text = $"Crush Viewer - {file.Name}";
    } catch (Exception ex) {
      MessageBox.Show($"Failed to load: {ex.Message}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Error);
    } finally {
      Enabled = true;
    }
  }

  private void _NavigateImage(int delta) {
    if (_imageCount < 2 || _currentFile == null)
      return;

    var newIndex = _currentIndex + delta;
    if (newIndex < 0 || newIndex >= _imageCount)
      return;

    _currentIndex = newIndex;
    var entry = FormatRegistry.GetEntry(_currentFormat);
    var raw = entry?.LoadRawImageAtIndex?.Invoke(_currentFile, _currentIndex);
    if (raw == null)
      return;

    _currentRawImage = raw;
    var oldBmp = _currentBitmap;
    _currentBitmap = BitmapConverter.RawImageToBitmap(raw);
    _imagePanel.Image = _currentBitmap;
    oldBmp?.Dispose();
    _UpdateStatusBar();
  }

  private void _UpdateStatusBar() {
    _formatLabel.Text = _currentFormat.ToString();
    if (_currentBitmap != null)
      _dimensionsLabel.Text = $"{_currentBitmap.Width} x {_currentBitmap.Height}";
    if (_currentFile != null)
      _fileSizeLabel.Text = _FormatSize(_currentFile.Length);
    _UpdateZoomLabel();
    _indexLabel.Text = _imageCount > 1 ? $"{_currentIndex + 1}/{_imageCount}" : "";
  }

  private void _UpdateZoomLabel() {
    _zoomLabel.Text = $"{_imagePanel.Zoom * 100:F0}%";
  }

  private static string _FormatSize(long bytes) => bytes switch {
    < 1024 => $"{bytes} B",
    < 1048576 => $"{bytes / 1024.0:F1} KiB",
    < 1073741824 => $"{bytes / 1048576.0:F1} MiB",
    _ => $"{bytes / 1073741824.0:F2} GiB",
  };

  private void _OnDragEnter(object? sender, DragEventArgs e) {
    if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
      e.Effect = DragDropEffects.Copy;
  }

  private void _OnDragDrop(object? sender, DragEventArgs e) {
    if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
      _LoadFile(new FileInfo(files[0]));
  }

  private void _OnKeyDown(object? sender, KeyEventArgs e) {
    switch (e.KeyCode) {
      case Keys.Oemplus or Keys.Add:
        _imagePanel.ZoomIn();
        e.Handled = true;
        break;
      case Keys.OemMinus or Keys.Subtract:
        _imagePanel.ZoomOut();
        e.Handled = true;
        break;
      case Keys.Left:
        _NavigateImage(-1);
        e.Handled = true;
        break;
      case Keys.Right:
        _NavigateImage(1);
        e.Handled = true;
        break;
    }
  }

  protected override void Dispose(bool disposing) {
    if (disposing) {
      _currentBitmap?.Dispose();
      _currentBitmap = null;
    }
    base.Dispose(disposing);
  }
}
