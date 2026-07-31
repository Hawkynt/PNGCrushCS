using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
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
  private readonly ToolStripStatusLabel _indexLabel;
  private readonly TrackBar _zoomSlider;
  private readonly TextBox _zoomTextBox;
  private bool _suppressZoomEvents;

  // Slider maps 0..2600 -> log2 -13..+13 -> zoom 1/8192..8192 (26 octaves, 100 ticks per octave).
  private const int _ZOOM_SLIDER_MIN = 0;
  private const int _ZOOM_SLIDER_MAX = 2600;
  private const int _ZOOM_SLIDER_CENTER = 1300;
  private const double _ZOOM_TICKS_PER_OCTAVE = 100.0;

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

  // Interactive crop state
  private int _cropTargetWidth;
  private int _cropTargetHeight;
  private InterpolationHint _cropInterpolation = InterpolationHint.Bicubic;
  private bool _cropResizeAfter; // true = crop+resize to target dimensions; false = crop only

  internal MainForm() {
    this.Text = "Crush Viewer";
    this.Size = new(1024, 768);
    this.StartPosition = FormStartPosition.CenterScreen;
    this.AllowDrop = true;
    this.KeyPreview = true;

    var menuStrip = this._CreateMenuStrip();
    this._statusBar = _CreateStatusBar(
      out this._formatLabel, out this._dimensionsLabel, out this._fileSizeLabel, out this._indexLabel,
      out this._zoomSlider, out this._zoomTextBox
    );
    this._imagePanel = new() { Dock = DockStyle.Fill };
    this._thumbnailStrip = new();
    this._thumbnailStrip.IndexSelected += this._NavigateToIndex;

    this.Controls.Add(this._imagePanel);
    this.Controls.Add(this._thumbnailStrip);
    this.Controls.Add(this._statusBar);
    this.Controls.Add(menuStrip);
    this.MainMenuStrip = menuStrip;

    this.DragEnter += this._OnDragEnter;
    this.DragDrop += this._OnDragDrop;
    this.KeyDown += this._OnKeyDown;
    this._imagePanel.ZoomChanged += this._OnImagePanelZoomChanged;
    this._imagePanel.CropConfirmed += this._OnCropConfirmed;
    this._imagePanel.CropCancelled += this._OnCropCancelled;

    this._zoomSlider.Scroll += this._OnZoomSliderScroll;
    this._zoomTextBox.KeyDown += this._OnZoomTextBoxKeyDown;
    this._zoomTextBox.Leave += this._OnZoomTextBoxLeave;
  }

  internal void OpenFileOnLoad(string path) => this._pendingFile = path;

  protected override void OnShown(EventArgs e) {
    base.OnShown(e);
    if (this._pendingFile != null)
      this._LoadFile(new(this._pendingFile));
  }

  private static Bitmap _IconFromText(string unicodeChar, Color color) {
    var bmp = new Bitmap(16, 16);
    using var g = Graphics.FromImage(bmp);
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    using var font = new Font("Segoe UI Emoji", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    var size = g.MeasureString(unicodeChar, font);
    g.DrawString(unicodeChar, font, new SolidBrush(color), (16 - size.Width) / 2, (16 - size.Height) / 2);
    return bmp;
  }

  private MenuStrip _CreateMenuStrip() {
    var menu = new MenuStrip();
    var menuColor = SystemColors.MenuText;
    var destructiveColor = Color.OrangeRed;

    var file = new ToolStripMenuItem("&File");
    file.DropDownItems.Add(new ToolStripMenuItem("&Open...", _IconFromText("\U0001F4C2", menuColor), (_, _) => this._OpenFileDialog()) { ShortcutKeys = Keys.Control | Keys.O });
    file.DropDownItems.Add(new ToolStripMenuItem("Save &As...", _IconFromText("\U0001F4BE", menuColor), (_, _) => this._SaveAsDialog()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S });
    file.DropDownItems.Add(new ToolStripSeparator());
    file.DropDownItems.Add(new ToolStripMenuItem("E&xit", _IconFromText("✖", destructiveColor), (_, _) => this.Close()) { ShortcutKeys = Keys.Alt | Keys.F4 });
    menu.Items.Add(file);

    var view = new ToolStripMenuItem("&View");
    view.DropDownItems.Add(new ToolStripMenuItem("Zoom &In", _IconFromText("➕", menuColor), (_, _) => this._imagePanel.ZoomIn()) { ShortcutKeys = Keys.Control | Keys.Oemplus });
    view.DropDownItems.Add(new ToolStripMenuItem("Zoom &Out", _IconFromText("➖", menuColor), (_, _) => this._imagePanel.ZoomOut()) { ShortcutKeys = Keys.Control | Keys.OemMinus });
    view.DropDownItems.Add(new ToolStripMenuItem("&Fit to Window", _IconFromText("⬜", menuColor), (_, _) => this._imagePanel.FitToWindow()) { ShortcutKeys = Keys.Control | Keys.D0 });
    view.DropDownItems.Add(new ToolStripMenuItem("&Actual Size (1:1)", _IconFromText("1⃣", menuColor), (_, _) => this._imagePanel.ActualSize()) { ShortcutKeys = Keys.Control | Keys.D1 });
    view.DropDownItems.Add(new ToolStripSeparator());
    var filterMenu = new ToolStripMenuItem("Display &Filter", _IconFromText("📺", menuColor));
    ToolStripMenuItem _MakeFilterItem(string label, FileFormat.Core.DisplayFilter? override_, bool initial = false) {
      var item = new ToolStripMenuItem(label) { CheckOnClick = false, Checked = initial };
      item.Click += (_, _) => {
        this._imagePanel.DisplayFilterOverride = override_;
        foreach (ToolStripMenuItem sibling in filterMenu.DropDownItems)
          sibling.Checked = ReferenceEquals(sibling, item);
      };
      return item;
    }
    filterMenu.DropDownItems.Add(_MakeFilterItem("&Format default", null, initial: true));
    filterMenu.DropDownItems.Add(_MakeFilterItem("&Off (no filter)", FileFormat.Core.DisplayFilter.None));
    filterMenu.DropDownItems.Add(_MakeFilterItem("NTSC &Composite", FileFormat.Core.DisplayFilter.NtscComposite));
    filterMenu.DropDownItems.Add(_MakeFilterItem("NTSC &S-Video (stub)", FileFormat.Core.DisplayFilter.NtscSvideo));
    filterMenu.DropDownItems.Add(_MakeFilterItem("&PAL (stub)", FileFormat.Core.DisplayFilter.Pal));
    view.DropDownItems.Add(filterMenu);
    view.DropDownItems.Add(new ToolStripSeparator());
    view.DropDownItems.Add(new ToolStripMenuItem("Choose &Text-Mode Font…", _IconFromText("🆎", menuColor), (_, _) => this._OnPickTextModeFont()));
    menu.Items.Add(view);

    var transform = new ToolStripMenuItem("&Transform");

    // Crop submenu
    var cropMenu = new ToolStripMenuItem("&Crop");
    cropMenu.DropDownItems.Add(new ToolStripMenuItem("&Free Crop", _IconFromText("✂", destructiveColor), this._OnFreeCrop) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.X });
    cropMenu.DropDownItems.Add(new ToolStripMenuItem("Crop to &Size...", _IconFromText("⬜", destructiveColor), this._OnInteractiveCrop) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.C });
    cropMenu.DropDownItems.Add(new ToolStripMenuItem("Crop with &Aspect Ratio...", _IconFromText("⬡", menuColor), this._OnAspectRatioCrop));
    transform.DropDownItems.Add(cropMenu);

    // Resize
    transform.DropDownItems.Add(new ToolStripMenuItem("&Resize...", _IconFromText("⇲", menuColor), this._OnResize) { ShortcutKeys = Keys.Control | Keys.R });
    transform.DropDownItems.Add(new ToolStripSeparator());

    // Rotate submenu
    var rotateMenu = new ToolStripMenuItem("R&otate");
    rotateMenu.DropDownItems.Add(new ToolStripMenuItem("90° &Clockwise", _IconFromText("↻", menuColor), (_, _) => this._ApplyRotate(RotateAngle.CW90)));
    rotateMenu.DropDownItems.Add(new ToolStripMenuItem("90° Counter-clock&wise", _IconFromText("↺", menuColor), (_, _) => this._ApplyRotate(RotateAngle.CW270)));
    rotateMenu.DropDownItems.Add(new ToolStripMenuItem("&180°", _IconFromText("⇄", menuColor), (_, _) => this._ApplyRotate(RotateAngle.CW180)));
    transform.DropDownItems.Add(rotateMenu);

    // Flip submenu
    var flipMenu = new ToolStripMenuItem("F&lip");
    flipMenu.DropDownItems.Add(new ToolStripMenuItem("&Horizontal", _IconFromText("⬌", menuColor), (_, _) => this._ApplyFlip(FlipDirection.Horizontal)));
    flipMenu.DropDownItems.Add(new ToolStripMenuItem("&Vertical", _IconFromText("⬍", menuColor), (_, _) => this._ApplyFlip(FlipDirection.Vertical)));
    transform.DropDownItems.Add(flipMenu);

    transform.DropDownItems.Add(new ToolStripSeparator());

    // Canvas Size
    transform.DropDownItems.Add(new ToolStripMenuItem("Canvas Si&ze...", _IconFromText("⬜", menuColor), this._OnCanvasSize));

    transform.DropDownItems.Add(new ToolStripSeparator());

    // Reduce Colors
    transform.DropDownItems.Add(new ToolStripMenuItem("Reduce Co&lors...", _IconFromText("\U0001F3A8", menuColor), this._OnReduceColors));

    menu.Items.Add(transform);

    var image = new ToolStripMenuItem("&Image");
    this._firstItem = new("&First", _IconFromText("⏮", menuColor), (_, _) => this._NavigateToIndex(0)) { ShortcutKeys = Keys.Control | Keys.Home, Enabled = false };
    this._prevItem = new("&Previous", _IconFromText("◀", menuColor), (_, _) => this._NavigateImage(-1)) { ShortcutKeyDisplayString = "Left", Enabled = false };
    this._nextItem = new("&Next", _IconFromText("▶", menuColor), (_, _) => this._NavigateImage(1)) { ShortcutKeyDisplayString = "Right", Enabled = false };
    this._lastItem = new("&Last", _IconFromText("⏭", menuColor), (_, _) => this._NavigateToIndex(this._imageCount - 1)) { ShortcutKeys = Keys.Control | Keys.End, Enabled = false };
    image.DropDownItems.AddRange([this._firstItem, this._prevItem, this._nextItem, this._lastItem]);
    menu.Items.Add(image);

    var help = new ToolStripMenuItem("&Help");
    help.DropDownItems.Add(new ToolStripMenuItem("&About", _IconFromText("ℹ", menuColor), (_, _) => MessageBox.Show(
      "Crush Viewer\nImage viewer supporting 500+ formats\n\nPart of PNGCrushCS",
      "About Crush Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information)));
    menu.Items.Add(help);

    return menu;
  }

  private static StatusStrip _CreateStatusBar(
    out ToolStripStatusLabel format, out ToolStripStatusLabel dimensions,
    out ToolStripStatusLabel fileSize, out ToolStripStatusLabel index,
    out TrackBar zoomSlider, out TextBox zoomTextBox
  ) {
    var bar = new StatusStrip();
    format = new("Ready") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    dimensions = new("") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    fileSize = new("") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    index = new("") { Spring = false, AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Right };
    var spacer = new ToolStripStatusLabel("") { Spring = true };

    zoomSlider = new() {
      Minimum = MainForm._ZOOM_SLIDER_MIN,
      Maximum = MainForm._ZOOM_SLIDER_MAX,
      Value = MainForm._ZOOM_SLIDER_CENTER,
      TickStyle = TickStyle.None,
      Width = 180,
      Height = 22,
      AutoSize = false,
      SmallChange = 10,
      LargeChange = 100,
    };
    zoomTextBox = new() {
      Width = 64,
      TextAlign = HorizontalAlignment.Right,
      Text = "100%",
      BorderStyle = BorderStyle.FixedSingle,
    };

    var sliderHost = new ToolStripControlHost(zoomSlider) { AutoSize = false, Size = new(180, 22), Margin = new(2, 0, 2, 0) };
    var textHost = new ToolStripControlHost(zoomTextBox) { AutoSize = false, Size = new(64, 20), Margin = new(2, 2, 4, 2) };

    bar.Items.AddRange([format, dimensions, fileSize, index, spacer, sliderHost, textHost]);
    return bar;
  }

  private void _OpenFileDialog() {
    using var dlg = new OpenFileDialog { Title = "Open Image", Filter = "All Files (*.*)|*.*" };
    if (dlg.ShowDialog() == DialogResult.OK)
      this._LoadFile(new(dlg.FileName));
  }

  private void _SaveAsDialog() {
    if (this._currentRawImage == null) {
      MessageBox.Show("No image loaded.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    var targets = FormatRegistry.ConversionTargets.OrderBy(e => e.Name).ToList();
    var filters = targets.Select(e => $"{e.Name} (*{e.PrimaryExtension})|*{e.PrimaryExtension}").ToList();

    // Preselect the format the user currently has open (or fall back to the first target).
    var currentTargetIdx = targets.FindIndex(e => e.Format == this._currentFormat);
    var initialFilterIndex = currentTargetIdx >= 0 ? currentTargetIdx + 1 : 1; // SaveFileDialog.FilterIndex is 1-based

    using var dlg = new SaveFileDialog { Title = "Save Image As", Filter = string.Join("|", filters), FilterIndex = initialFilterIndex };
    if (dlg.ShowDialog() != DialogResult.OK) return;

    // Resolve target entry — the filter is always one of the registered formats (no "All Files" option).
    var selectedIndex = dlg.FilterIndex - 1;
    var targetEntry = selectedIndex >= 0 && selectedIndex < targets.Count
      ? targets[selectedIndex]
      : FormatRegistry.GetEntry(FormatRegistry.DetectFromExtension(Path.GetExtension(dlg.FileName).ToLowerInvariant()));

    // Text-mode formats (NFO/ANSI/XBIN) drive their own grid via the FontCodepageWindow picker
    // instead of the VideoMode/resize/reduce pipeline. The picker captures font + codepage + cell
    // grid; we resize the source to (cols*cellW)×(rows*cellH) and let the format writer quantize.
    var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
    if (ext is ".nfo" or ".diz" or ".ans" or ".ansi" or ".xb" or ".xbin") {
      using var fontDlg = new Hawkynt.ImageTransformUI.FontCodepageWindow();
      var defaultCols = Math.Max(1, this._currentRawImage.Width / 8);
      var defaultRows = Math.Max(1, this._currentRawImage.Height / 16);
      fontDlg.SetDefaults(Math.Min(defaultCols, 200), Math.Min(defaultRows, 100));
      if (fontDlg.ShowDialog(this) != DialogResult.OK) return;

      if (fontDlg.PickedFont is not null)
        FileFormat.TextMode.BitmapFont.Default = fontDlg.PickedFont;

      var targetW = fontDlg.PickedColumns * FileFormat.TextMode.BitmapFont.Default.CellWidth;
      var targetH = fontDlg.PickedRows * FileFormat.TextMode.BitmapFont.Default.CellHeight;
      if (this._currentRawImage.Width != targetW || this._currentRawImage.Height != targetH)
        this._currentRawImage = ImageTransformer.Resize(this._currentRawImage, targetW, targetH, ResizeMode.Stretch, InterpolationHint.Bilinear);
      this._currentBitmap?.Dispose();
      this._currentBitmap = BitmapConverter.RawImageToBitmap(this._currentRawImage);
      this._imagePanel.Image = this._currentBitmap;

      try {
        if (targetEntry?.ConvertFromRawImage is null) {
          MessageBox.Show($"Format '{targetEntry?.Name ?? ext}' does not support writing.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }
        File.WriteAllBytes(dlg.FileName, targetEntry.ConvertFromRawImage(this._currentRawImage));
        this._formatLabel.Text = "Saved";
      } catch (Exception ex) {
        MessageBox.Show($"Save failed: {ex.Message}", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
      return;
    }

    // VideoMode-aware path: when the target declares 2+ video modes, let the user pick one.
    // Single-mode formats auto-pick. Formats that declare none (legacy/external) skip mode-specific steps.
    VideoMode? pickedMode = null;
    if (targetEntry?.VideoModes is { Length: > 0 } modes) {
      if (modes.Length == 1) {
        pickedMode = modes[0];
      } else {
        var pre = SaveAsPlanner.PickClosestMode(targetEntry, this._currentRawImage.Width, this._currentRawImage.Height)!;
        var preIdx = Array.IndexOf(modes, pre);
        using var modeDlg = new VideoModeDialog(modes, preIdx, this._currentRawImage.Width, this._currentRawImage.Height);
        if (modeDlg.ShowDialog(this) != DialogResult.OK) return;
        pickedMode = modeDlg.PickedMode;
      }
    }

    // Resize prompt — only when the chosen mode doesn't cover the source dimensions.
    if (pickedMode != null && SaveAsPlanner.NeedsResizePromptInMode(pickedMode, this._currentRawImage)) {
      var result = MessageBox.Show(
        $"The target format requires specific dimensions.\nCurrent image: {this._currentRawImage.Width}x{this._currentRawImage.Height}\n\nWould you like to resize/crop first?\n\n" +
        "Yes = Open Resize dialog\nNo = Continue without resizing",
        "Fixed Resolution Format", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
      if (result == DialogResult.Yes) {
        this._OpenResizeDialog(pickedMode.Dimensions);
        if (this._currentRawImage == null) return;
      }
    }

    // Colour reduction — derived entirely from the chosen mode's palette constraints.
    if (pickedMode != null) {
      var plan = SaveAsPlanner.PlanReductionInMode(pickedMode, this._currentRawImage);
      if (plan.NeedsReduction && !this._ApplyReduceColors(plan.AllowedRanges, plan.FixedPalettes))
        return;
    }

    try {
      if (targetEntry == null) { MessageBox.Show($"Cannot determine output format for '{Path.GetExtension(dlg.FileName)}'.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
      if (targetEntry.ConvertFromRawImage == null) { MessageBox.Show($"Format '{targetEntry.Name}' does not support writing.", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
      try {
        File.WriteAllBytes(dlg.FileName, targetEntry.ConvertFromRawImage(this._currentRawImage));
        PaletteSidecar.TryWrite(dlg.FileName, this._currentRawImage);
      } catch (Exception convEx) {
        // Conversion failed — most commonly because the image has too many colours for the target format.
        // Offer color reduction as a retry, with the target format's actual palette-size constraint applied.
        var retry = MessageBox.Show(
          $"Saving failed: {convEx.Message}\n\nThis may be because the image has too many colors for the target format.\n\nWould you like to reduce colors and try again?",
          "Save As", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (retry != DialogResult.Yes) return;
        var retryRanges = pickedMode?.AllowedPaletteRanges ?? [new(2, 256)];
        var retryFixedPalettes = pickedMode?.AvailablePalettes;
        if (!this._ApplyReduceColors(retryRanges, retryFixedPalettes)) return;
        File.WriteAllBytes(dlg.FileName, targetEntry.ConvertFromRawImage(this._currentRawImage));
        PaletteSidecar.TryWrite(dlg.FileName, this._currentRawImage);
      }

      this._formatLabel.Text = "Saved";
    } catch (Exception ex) {
      MessageBox.Show($"Save failed: {ex.Message}", "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void _OnResize(object? sender, EventArgs e) => this._OpenResizeDialog(allowedDimensions: null);

  /// <summary>Opens the resize dialog. When called from the Save-As flow with a target format,
  /// <paramref name="allowedDimensions"/> constrains the resize to dimensions the format actually accepts.</summary>
  private void _OpenResizeDialog((IntegerRange Width, IntegerRange Height)[]? allowedDimensions) {
    if (this._currentRawImage == null) return;
    var hint = ImageTransformer.GuessInterpolation(this._currentRawImage);
    using var dlg = new ResizeDialog(this._currentRawImage.Width, this._currentRawImage.Height, hint, allowedDimensions);
    var result = dlg.ShowDialog(this);

    if (result == DialogResult.Retry) {
      // "Select on Image" was clicked from the CropRegion mode
      this._cropTargetWidth = 0;
      this._cropTargetHeight = 0;
      this._cropResizeAfter = false;
      this._cropInterpolation = InterpolationHint.Bicubic;

      // Pre-populate from dialog crop values if they had something
      RectangleF? initial = null;
      if (dlg.CropWidth > 0 && dlg.CropHeight > 0)
        initial = new RectangleF(dlg.CropX, dlg.CropY, dlg.CropWidth, dlg.CropHeight);

      this._EnterCropMode(0, initial);
      return;
    }

    if (result != DialogResult.OK) return;

    if (dlg.Mode == ResizeMode.CropRegion) {
      this._currentRawImage = ImageTransformer.Crop(this._currentRawImage, new(dlg.CropX, dlg.CropY, dlg.CropWidth, dlg.CropHeight));
    } else {
      this._currentRawImage = ImageTransformer.Resize(this._currentRawImage, dlg.TargetWidth, dlg.TargetHeight, dlg.Mode, dlg.Interpolation,
        Rgba32.FromArgb(dlg.LetterboxColor.A, dlg.LetterboxColor.R, dlg.LetterboxColor.G, dlg.LetterboxColor.B));
    }

    this._currentBitmap?.Dispose();
    this._currentBitmap = BitmapConverter.RawImageToBitmap(this._currentRawImage);
    this._imagePanel.Image = this._currentBitmap;
    this._UpdateStatusBar();
  }

  private void _OnInteractiveCrop(object? sender, EventArgs e) {
    if (this._currentRawImage == null) return;

    using var dlg = new CropToSizeDialog(this._currentRawImage.Width, this._currentRawImage.Height);
    if (dlg.ShowDialog(this) != DialogResult.OK) return;

    this._cropTargetWidth = dlg.TargetWidth;
    this._cropTargetHeight = dlg.TargetHeight;
    this._cropResizeAfter = dlg.ResizeAfterCrop;
    this._cropInterpolation = dlg.Interpolation;

    var aspect = this._cropTargetWidth / (float)this._cropTargetHeight;
    this._EnterCropMode(aspect, null);
  }

  private void _OnFreeCrop(object? sender, EventArgs e) {
    if (this._currentRawImage == null) return;

    this._cropTargetWidth = 0;
    this._cropTargetHeight = 0;
    this._cropResizeAfter = false;
    this._cropInterpolation = InterpolationHint.Bicubic;
    this._EnterCropMode(0, null);
  }

  private void _OnAspectRatioCrop(object? sender, EventArgs e) {
    if (this._currentRawImage == null) return;

    using var dlg = new AspectRatioCropDialog();
    if (dlg.ShowDialog(this) != DialogResult.OK) return;

    this._cropTargetWidth = 0;
    this._cropTargetHeight = 0;
    this._cropResizeAfter = false;
    this._cropInterpolation = InterpolationHint.Bicubic;
    this._EnterCropMode(dlg.AspectRatio, null);
  }

  private void _ApplyRotate(RotateAngle angle) {
    if (this._currentRawImage == null) return;

    this._currentRawImage = ImageTransformer.Rotate(this._currentRawImage, angle);
    this._currentBitmap?.Dispose();
    this._currentBitmap = BitmapConverter.RawImageToBitmap(this._currentRawImage);
    this._imagePanel.Image = this._currentBitmap;
    this._UpdateStatusBar();
  }

  private void _ApplyFlip(FlipDirection direction) {
    if (this._currentRawImage == null) return;

    this._currentRawImage = ImageTransformer.Flip(this._currentRawImage, direction);
    this._currentBitmap?.Dispose();
    this._currentBitmap = BitmapConverter.RawImageToBitmap(this._currentRawImage);
    this._imagePanel.Image = this._currentBitmap;
    this._UpdateStatusBar();
  }

  private void _OnCanvasSize(object? sender, EventArgs e) {
    if (this._currentRawImage == null) return;

    using var dlg = new CanvasSizeDialog(this._currentRawImage.Width, this._currentRawImage.Height);
    if (dlg.ShowDialog(this) != DialogResult.OK) return;

    this._currentRawImage = ImageTransformer.ExtendCanvas(this._currentRawImage, dlg.TargetWidth, dlg.TargetHeight, dlg.Anchor,
      Rgba32.FromArgb(dlg.FillColor.A, dlg.FillColor.R, dlg.FillColor.G, dlg.FillColor.B));
    this._currentBitmap?.Dispose();
    this._currentBitmap = BitmapConverter.RawImageToBitmap(this._currentRawImage);
    this._imagePanel.Image = this._currentBitmap;
    this._UpdateStatusBar();
  }

  private void _OnReduceColors(object? sender, EventArgs e) {
    if (this._currentRawImage == null) return;

    this._ApplyReduceColors(allowedRanges: null, fixedPalettes: null);
  }

  /// <summary>Opens the color reduction dialog and applies the result to the current image.</summary>
  /// <param name="allowedRanges">If non-null, constrains the palette-size slider to these disjoint ranges.</param>
  /// <param name="fixedPalettes">If non-null and non-empty, hides the quantizer UI and shows a palette dropdown with these palettes.</param>
  /// <returns>True if the user applied a reduction, false if cancelled.</returns>
  private bool _ApplyReduceColors(IntegerRange[]? allowedRanges, FixedPalette[]? fixedPalettes) {
    if (this._currentRawImage == null) return false;
    using var bmp = BitmapConverter.RawImageToBitmap(this._currentRawImage);
    using var colorDlg = new Hawkynt.ImageTransformUI.ReduceColorsWindow(bmp);

    // Apply BOTH constraints when both are declared (e.g. NES: master palette of 64 + per-image
    // limit of 4 → subset picker). Apply ranges first so SetFixedPalettes can read the max when
    // deciding whether to activate the subset picker.
    if (allowedRanges is { Length: > 0 }) {
      var tuples = new (int Min, int Max)[allowedRanges.Length];
      for (var i = 0; i < allowedRanges.Length; ++i)
        tuples[i] = (allowedRanges[i].Min, allowedRanges[i].Max);
      colorDlg.SetAllowedPaletteRanges(tuples);
    }
    if (fixedPalettes is { Length: > 0 }) {
      var palTuples = new (string Name, byte[] PackedRgb)[fixedPalettes.Length];
      for (var i = 0; i < fixedPalettes.Length; ++i)
        palTuples[i] = (fixedPalettes[i].Name, fixedPalettes[i].ToPackedRgb());
      colorDlg.SetFixedPalettes(palTuples);
    }

    if (colorDlg.ShowDialog(this) != DialogResult.OK) return false;

    var quantName = colorDlg.PickedQuantizerName;
    var ditherName = colorDlg.PickedDithererName;
    var paletteSize = colorDlg.PaletteSize;
    if (quantName == null || ditherName == null) return false;

    this._currentRawImage = BitmapConverter.QuantizeRawImage(
      this._currentRawImage, paletteSize, quantName, ditherName,
      isHighQuality: true,  // match the preview path in ReduceColorsWindow (was false → save diverged from preview)
      quantizerParams: colorDlg.PickedQuantizerParams,
      dithererParams: colorDlg.PickedDithererParams
    );
    this._currentBitmap?.Dispose();
    this._currentBitmap = BitmapConverter.RawImageToBitmap(this._currentRawImage);
    this._imagePanel.Image = this._currentBitmap;
    this._UpdateStatusBar();
    return true;
  }

  private void _EnterCropMode(float aspectRatio, RectangleF? initial) {
    this._imagePanel.ShowCropRect(aspectRatio, initial);
    this._formatLabel.Text = "Crop: drag to position, resize handles to adjust. Enter = apply, Escape = cancel.";
  }

  private void _OnCropConfirmed() {
    if (this._currentRawImage == null) return;
    var rect = this._imagePanel.GetCropRect();
    this._imagePanel.HideCropRect();

    // Convert to integer rectangle
    var cropRegion = new Rectangle(
      (int)Math.Round(rect.X),
      (int)Math.Round(rect.Y),
      (int)Math.Round(rect.Width),
      (int)Math.Round(rect.Height)
    );

    if (cropRegion.Width < 1 || cropRegion.Height < 1) {
      this._formatLabel.Text = "Crop cancelled (selection too small).";
      this._UpdateStatusBar();
      return;
    }

    // Step 1: Crop
    this._currentRawImage = ImageTransformer.Crop(this._currentRawImage,
      new PixelRect(cropRegion.X, cropRegion.Y, cropRegion.Width, cropRegion.Height));

    // Step 2: Resize to target dimensions if requested
    if (this._cropResizeAfter && this._cropTargetWidth > 0 && this._cropTargetHeight > 0) {
      if (this._currentRawImage.Width != this._cropTargetWidth || this._currentRawImage.Height != this._cropTargetHeight) {
        this._currentRawImage = ImageTransformer.Resize(this._currentRawImage, this._cropTargetWidth, this._cropTargetHeight, ResizeMode.Stretch, this._cropInterpolation);
      }
    }

    this._currentBitmap?.Dispose();
    this._currentBitmap = BitmapConverter.RawImageToBitmap(this._currentRawImage);
    this._imagePanel.Image = this._currentBitmap;
    this._UpdateStatusBar();
  }

  private void _OnCropCancelled() {
    this._imagePanel.HideCropRect();
    this._UpdateStatusBar();
  }

  private void _OnPickTextModeFont() {
    using var fontDlg = new Hawkynt.ImageTransformUI.FontCodepageWindow();
    fontDlg.Text = "Choose text-mode font";
    if (fontDlg.ShowDialog(this) != DialogResult.OK || fontDlg.PickedFont is null) return;
    FileFormat.TextMode.BitmapFont.Default = fontDlg.PickedFont;
    // Re-render: if a text-mode file is currently loaded, re-load it through the format pipeline so
    // the new font's glyphs replace the rendered bitmap. Otherwise nothing visible changes.
    if (this._currentFile is null) return;
    var ext = this._currentFile.Extension.ToLowerInvariant();
    if (ext is ".nfo" or ".diz" or ".ans" or ".ansi" or ".xb" or ".xbin")
      this._LoadFile(this._currentFile);
  }

  private async void _LoadFile(FileInfo file) {
    this._loadCts?.Cancel();
    this._loadCts?.Dispose();
    this._loadCts = new();
    var ct = this._loadCts.Token;

    // Text-mode formats need a font to render — let the user pick one before parsing so the choice
    // flows through to the format's ToRawImage (which reads BitmapFont.Default). User can cancel out
    // and keep whatever font is currently active.
    var ext = file.Extension.ToLowerInvariant();
    if (ext is ".nfo" or ".diz" or ".ans" or ".ansi" or ".xb" or ".xbin") {
      using var fontDlg = new Hawkynt.ImageTransformUI.FontCodepageWindow();
      fontDlg.Text = $"Font for {file.Name}";
      if (fontDlg.ShowDialog(this) == DialogResult.OK && fontDlg.PickedFont is not null)
        FileFormat.TextMode.BitmapFont.Default = fontDlg.PickedFont;
    }

    this._formatLabel.Text = $"Loading {file.Name}...";
    this.Enabled = false;
    try {
      var (format, rawImage, bitmap, imageCount) = await Task.Run(() => {
        var fmt = ImageFormatDetector.Detect(file);
        if (fmt == ImageFormat.Unknown) return (fmt, (RawImage?)null, (Bitmap?)null, 0);
        ct.ThrowIfCancellationRequested();
        var raw = BitmapConverter.LoadRawImage(file, fmt);
        ct.ThrowIfCancellationRequested();
        // If the loaded image is indexed AND a .pal sidecar exists next to the file, replace
        // the default palette with the saved one. Lets formats like NES CHR (which don't store
        // a palette on-disk) round-trip the colours the user picked at save time.
        if (raw != null) raw = PaletteSidecar.Apply(file.FullName, raw);
        var bmp = raw != null ? BitmapConverter.RawImageToBitmap(raw) : BitmapConverter.LoadBitmap(file, fmt);
        var entry = FormatRegistry.GetEntry(fmt);
        var count = entry?.GetImageCount?.Invoke(file) ?? 0;
        if (count < 2) count = 0;
        return (fmt, raw, bmp, count);
      }, ct);

      if (ct.IsCancellationRequested) return;
      if (format == ImageFormat.Unknown) { MessageBox.Show($"Unknown image format: {file.Name}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
      if (rawImage == null && bitmap == null) { MessageBox.Show($"Format detected ({format}) but could not decode: {file.Name}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

      this._currentFormat = format;
      this._currentFile = file;
      this._currentRawImage = rawImage;
      var oldBmp = this._currentBitmap;
      this._currentBitmap = bitmap;
      this._imagePanel.Image = this._currentBitmap;

      // Apply VideoMode display hints (PixelAspectRatio + DisplayFilter) for the loaded format.
      // When the format declares one or more modes, the first mode's hints drive the viewer's display.
      var loadedEntry = FormatRegistry.GetEntry(format);
      var loadedMode = loadedEntry?.VideoModes is { Length: > 0 } modes && rawImage != null
        ? SaveAsPlanner.PickClosestMode(loadedEntry, rawImage.Width, rawImage.Height)
        : null;
      this._imagePanel.SetVideoModeHints(loadedMode?.PixelAspectRatio, loadedMode?.DisplayFilter ?? FileFormat.Core.DisplayFilter.None);
      oldBmp?.Dispose();

      this._imageCount = imageCount;
      this._currentIndex = 0;

      this._UpdateMultiImageUI();
      this._UpdateStatusBar();
      this.Text = $"Crush Viewer - {file.Name}";
    } catch (OperationCanceledException) {
    } catch (Exception ex) {
      MessageBox.Show($"Failed to load: {ex.Message}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Error);
    } finally {
      this.Enabled = true;
    }
  }

  private void _NavigateImage(int delta) {
    if (this._imageCount < 2 || this._currentFile == null) return;

    this._NavigateToIndex(Math.Clamp(this._currentIndex + delta, 0, this._imageCount - 1));
  }

  private void _NavigateToIndex(int index) {
    if (this._imageCount < 2 || this._currentFile == null) return;
    if (index < 0 || index >= this._imageCount || index == this._currentIndex) return;

    this._currentIndex = index;
    var entry = FormatRegistry.GetEntry(this._currentFormat);
    var raw = entry?.LoadRawImageAtIndex?.Invoke(this._currentFile, this._currentIndex);
    if (raw == null) return;

    this._currentRawImage = raw;
    var oldBmp = this._currentBitmap;
    this._currentBitmap = BitmapConverter.RawImageToBitmap(raw);
    this._imagePanel.Image = this._currentBitmap;
    oldBmp?.Dispose();

    this._thumbnailStrip.Select(this._currentIndex);
    this._UpdateStatusBar();
    this._UpdateNavigationState();
  }

  private void _UpdateMultiImageUI() {
    this._UpdateNavigationState();

    if (this._imageCount >= 2) {
      var entry = FormatRegistry.GetEntry(this._currentFormat);
      var file = this._currentFile!;
      this._thumbnailStrip.SetSource(
        this._imageCount, async (i, ct) => await Task.Run(() => {
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
      this._thumbnailStrip.Clear();
    }
  }

  private void _UpdateNavigationState() {
    var m = this._imageCount >= 2;
    this._firstItem.Enabled = m && this._currentIndex > 0;
    this._prevItem.Enabled = m && this._currentIndex > 0;
    this._nextItem.Enabled = m && this._currentIndex < this._imageCount - 1;
    this._lastItem.Enabled = m && this._currentIndex < this._imageCount - 1;
  }

  private void _UpdateStatusBar() {
    this._formatLabel.Text = this._currentFormat.ToString();
    if (this._currentBitmap != null)
      this._dimensionsLabel.Text = $"{this._currentBitmap.Width} x {this._currentBitmap.Height}";
    if (this._currentFile != null)
      this._fileSizeLabel.Text = _FormatSize(this._currentFile.Length);
    this._OnImagePanelZoomChanged(this._imagePanel.Zoom);
    this._indexLabel.Text = this._imageCount > 1 ? $"{this._currentIndex + 1}/{this._imageCount}" : "";
  }

  private void _OnImagePanelZoomChanged(float zoom) {
    this._suppressZoomEvents = true;
    try {
      var sliderPos = _ZoomToSliderPosition(zoom);
      if (sliderPos >= this._zoomSlider.Minimum && sliderPos <= this._zoomSlider.Maximum)
        this._zoomSlider.Value = sliderPos;
      else
        this._zoomSlider.Value = sliderPos < this._zoomSlider.Minimum ? this._zoomSlider.Minimum : this._zoomSlider.Maximum;
      if (!this._zoomTextBox.Focused)
        this._zoomTextBox.Text = _FormatZoom(zoom);
    } finally {
      this._suppressZoomEvents = false;
    }
  }

  private void _OnZoomSliderScroll(object? sender, EventArgs e) {
    if (this._suppressZoomEvents) return;
    var zoom = _SliderPositionToZoom(this._zoomSlider.Value);
    this._imagePanel.SetZoom(zoom);
  }

  private void _OnZoomTextBoxKeyDown(object? sender, KeyEventArgs e) {
    if (e.KeyCode != Keys.Enter) return;
    e.Handled = true;
    e.SuppressKeyPress = true;
    this._ApplyZoomFromTextBox();
  }

  private void _OnZoomTextBoxLeave(object? sender, EventArgs e) => this._zoomTextBox.Text = _FormatZoom(this._imagePanel.Zoom);

  private void _ApplyZoomFromTextBox() {
    var parsed = _ParseZoomInput(this._zoomTextBox.Text);
    if (parsed is { } z)
      this._imagePanel.SetZoom(z);
    else
      this._zoomTextBox.Text = _FormatZoom(this._imagePanel.Zoom);
  }

  private static int _ZoomToSliderPosition(float zoom) {
    if (zoom <= 0) return MainForm._ZOOM_SLIDER_MIN;
    var pos = (Math.Log2(zoom) * MainForm._ZOOM_TICKS_PER_OCTAVE) + MainForm._ZOOM_SLIDER_CENTER;
    return (int)Math.Round(Math.Clamp(pos, MainForm._ZOOM_SLIDER_MIN, MainForm._ZOOM_SLIDER_MAX));
  }

  private static float _SliderPositionToZoom(int position) =>
    (float)Math.Pow(2, (position - MainForm._ZOOM_SLIDER_CENTER) / MainForm._ZOOM_TICKS_PER_OCTAVE);

  private static string _FormatZoom(float zoom) {
    var pct = zoom * 100.0;
    if (pct >= 100) return pct.ToString("F0", CultureInfo.CurrentCulture) + "%";
    if (pct >= 10) return pct.ToString("F1", CultureInfo.CurrentCulture) + "%";
    if (pct >= 1) return pct.ToString("F2", CultureInfo.CurrentCulture) + "%";
    return pct.ToString("G3", CultureInfo.CurrentCulture) + "%";
  }

  private static float? _ParseZoomInput(string? text) {
    if (string.IsNullOrWhiteSpace(text)) return null;
    var s = text.Trim().ToLowerInvariant();
    var isMultiplier = s.EndsWith('x');
    s = s.TrimEnd('x', '%', ' ');
    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)
        && !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
      return null;
    if (!isMultiplier) v /= 100.0;
    return v > 0 ? (float)v : null;
  }

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
      this._LoadFile(new(files[0]));
  }

  private void _OnKeyDown(object? sender, KeyEventArgs e) {
    switch (e.KeyCode) {
      case Keys.Oemplus or Keys.Add:
        this._imagePanel.ZoomIn(); e.Handled = true; break;
      case Keys.OemMinus or Keys.Subtract:
        this._imagePanel.ZoomOut(); e.Handled = true; break;
      case Keys.Left:
        this._NavigateImage(-1); e.Handled = true; break;
      case Keys.Right:
        this._NavigateImage(1); e.Handled = true; break;
      case Keys.PageUp:
        this._NavigateImage(-10); e.Handled = true; break;
      case Keys.PageDown:
        this._NavigateImage(10); e.Handled = true; break;
      case Keys.Home:
        this._NavigateToIndex(0); e.Handled = true; break;
      case Keys.End:
        this._NavigateToIndex(this._imageCount - 1); e.Handled = true; break;
    }
  }

  protected override void Dispose(bool disposing) {
    if (disposing) {
      this._loadCts?.Cancel();
      this._loadCts?.Dispose();
      this._loadCts = null;
      this._thumbnailStrip.Clear();
      this._currentBitmap?.Dispose();
      this._currentBitmap = null;
    }
    base.Dispose(disposing);
  }
}
