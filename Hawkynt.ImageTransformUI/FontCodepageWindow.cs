using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using FileFormat.TextMode;

namespace Hawkynt.ImageTransformUI;

/// <summary>
/// Modal dialog for choosing a bitmap font + code page + cell grid dimensions when saving to
/// text-mode formats (NFO / ANSI / XBIN). Mirrors the <see cref="ReduceColorsWindow"/> pattern:
/// caller seeds defaults and reads back the user's choices via public properties after
/// <see cref="Form.ShowDialog()"/> returns <see cref="DialogResult.OK"/>.
/// </summary>
public sealed class FontCodepageWindow : Form {

  private const string _SystemFontPrefix = "[System] ";
  private const string _EmbeddedFontPrefix = "[Embedded] ";
  private static readonly (int W, int H, string Label)[] _CellSizes = [
    (8, 8,  "8 × 8 (CGA mode)"),
    (8, 14, "8 × 14 (EGA)"),
    (8, 16, "8 × 16 (VGA, default)"),
    (8, 12, "8 × 12 (compact)"),
    (8, 19, "8 × 19 (large)"),
    (8, 24, "8 × 24 (giant)"),
  ];

  private readonly ComboBox _fontCombo;
  private readonly ComboBox _cellSizeCombo;
  private readonly ComboBox _codepageCombo;
  private readonly NumericUpDown _columnsInput;
  private readonly NumericUpDown _rowsInput;
  private readonly Button _loadFontButton;
  private readonly Button _okButton;
  private readonly Button _cancelButton;
  private readonly _NearestNeighborPictureBox _previewBox;

  /// <summary>PictureBox subclass that paints its <see cref="PictureBox.Image"/> with nearest-neighbor
  /// interpolation centred and fit-to-box (replicating <see cref="PictureBoxSizeMode.Zoom"/>'s scaling
  /// behaviour). Bitmap-font glyphs scale crisply pixel-on-pixel instead of bilinear-blurred.
  /// Double-buffered + ResizeRedraw so dragging the splitter or the window edge re-renders cleanly
  /// without leaving stale pixels around the scaled image.</summary>
  private sealed class _NearestNeighborPictureBox : PictureBox {

    public _NearestNeighborPictureBox() {
      this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      this.DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) {
      // Background is always cleared to BackColor inside OnPaint — skipping here avoids the default
      // 2-step erase+paint that flickers during resize.
    }

    protected override void OnPaint(PaintEventArgs e) {
      using (var bg = new SolidBrush(this.BackColor))
        e.Graphics.FillRectangle(bg, this.ClientRectangle);

      var img = this.Image;
      if (img is null) return;
      float bw = this.ClientSize.Width, bh = this.ClientSize.Height;
      if (bw < 1 || bh < 1) return;

      e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
      e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
      e.Graphics.SmoothingMode = SmoothingMode.None;
      float iw = img.Width, ih = img.Height;
      var scale = Math.Min(bw / iw, bh / ih);
      var dw = iw * scale;
      var dh = ih * scale;
      if (dw < 1 || dh < 1) return;
      var dx = (bw - dw) / 2;
      var dy = (bh - dh) / 2;
      e.Graphics.DrawImage(img, new RectangleF(dx, dy, dw, dh));
    }
  }
  private readonly Label _statusLabel;

  // Resolved BitmapFont for each (fontName, cellW, cellH) combo. System fonts are rasterised on
  // demand and cached here so re-selecting the same font is instant.
  private readonly Dictionary<string, BitmapFont> _fontCache = new();

  // Embedded font name → (accessor, natural cellW, cellH). Selecting an embedded font auto-snaps
  // the cell-size combo to the font's natural dimensions so the preview always renders correctly.
  private readonly Dictionary<string, (Func<BitmapFont> Get, int CellW, int CellH)> _embeddedAccessors = new();

  // Names shown in the font combo: embedded era fonts first, then system fonts.
  private readonly string[] _allFontNames;

  /// <summary>The font the user picked (null until OK).</summary>
  public BitmapFont? PickedFont { get; private set; }

  /// <summary>The code page identifier the user picked (e.g. "CP437"). Always set on OK.</summary>
  public string PickedCodepage { get; private set; } = "CP437";

  public int PickedColumns { get; private set; } = 80;
  public int PickedRows { get; private set; } = 25;

  public FontCodepageWindow() {
    this.Text = "Font and code page";
    this.ClientSize = new Size(820, 560);
    this.MinimumSize = new Size(640, 440);
    this.StartPosition = FormStartPosition.CenterParent;
    // Sizable so the user can grow the preview area to give 8x16 fonts more room. The splitter
    // between the controls and the preview can be dragged to balance them however they like.
    this.FormBorderStyle = FormBorderStyle.Sizable;
    this.MaximizeBox = true;
    this.MinimizeBox = true;

    // Embedded era catalogue at the top of the dropdown (lazy-loaded — selecting one
    // triggers the deflate decompression in BitmapFontEmbedded).
    var combined = new List<string>();
    foreach (var (label, get, cellW, cellH) in BitmapFontEmbedded.All) {
      var name = _EmbeddedFontPrefix + label;
      combined.Add(name);
      _embeddedAccessors[name] = (get, cellW, cellH);
    }

    var systemFamilies = BitmapFontRasterizer.GetInstalledMonospaceFamilies();
    foreach (var fam in systemFamilies) combined.Add(_SystemFontPrefix + fam);
    _allFontNames = combined.ToArray();

    var fontLabel = new Label { Text = "Font:", Top = 0, Left = 0, AutoSize = true };
    _fontCombo = new ComboBox {
      Top = 18, Left = 0, Width = 270, DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = false, MaxDropDownItems = 20,
      Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };
    _fontCombo.Items.AddRange(_allFontNames);
    _fontCombo.SelectedIndex = 0;
    _fontCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();

    _loadFontButton = new Button { Text = "Load .F16…", Top = 47, Left = 0, Width = 120, Height = 23 };
    _loadFontButton.Click += this._OnLoadFontClick;

    var cellSizeLabel = new Label { Text = "Cell size:", Top = 80, Left = 0, AutoSize = true };
    _cellSizeCombo = new ComboBox {
      Top = 98, Left = 0, Width = 270, DropDownStyle = ComboBoxStyle.DropDownList,
      Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };
    foreach (var (_, _, lbl) in _CellSizes) _cellSizeCombo.Items.Add(lbl);
    _cellSizeCombo.SelectedIndex = 2; // 8x16 default
    _cellSizeCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();

    var codepageLabel = new Label { Text = "Code page:", Top = 130, Left = 0, AutoSize = true };
    _codepageCombo = new ComboBox {
      Top = 148, Left = 0, Width = 270, DropDownStyle = ComboBoxStyle.DropDownList,
      Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };
    _codepageCombo.Items.AddRange(["CP437 (IBM PC original)", "CP850 (Western European)", "CP866 (Cyrillic)"]);
    _codepageCombo.SelectedIndex = 0;
    _codepageCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();

    var colsLabel = new Label { Text = "Columns:", Top = 180, Left = 0, AutoSize = true };
    _columnsInput = new NumericUpDown {
      Top = 198, Left = 0, Width = 80, Minimum = 1, Maximum = 4096, Value = 80,
    };
    _columnsInput.ValueChanged += (_, _) => this._UpdatePreview();

    var rowsLabel = new Label { Text = "Rows:", Top = 180, Left = 100, AutoSize = true };
    _rowsInput = new NumericUpDown {
      Top = 198, Left = 100, Width = 80, Minimum = 1, Maximum = 4096, Value = 25,
    };
    _rowsInput.ValueChanged += (_, _) => this._UpdatePreview();

    _statusLabel = new Label {
      Top = 240, Left = 0, AutoSize = false, ForeColor = Color.DarkGray,
      Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
      Text = "Built-in is the procedural VGA-style font.\nAny installed system font can also be picked — TrueType\nmonospace fonts (Consolas, Cascadia Mono, Mxoldschool\nPC fonts) work best.",
    };

    var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
    leftPanel.Controls.AddRange([fontLabel, _fontCombo, _loadFontButton, cellSizeLabel, _cellSizeCombo, codepageLabel, _codepageCombo, colsLabel, _columnsInput, rowsLabel, _rowsInput, _statusLabel]);
    // Status label fills the bottom of the left panel — set width once after layout so the anchor wires up.
    leftPanel.Layout += (_, _) => {
      _statusLabel.Width = leftPanel.ClientSize.Width - 20;
      _statusLabel.Height = Math.Max(60, leftPanel.ClientSize.Height - _statusLabel.Top - 10);
    };

    // Preview lives in the right panel — label at top, picture box fills the rest with Zoom mode
    // so the 16×16 charset bitmap scales smoothly as the splitter / window grows.
    var previewLabel = new Label { Text = "Character set (16 × 16 grid, all 256 code points):", Dock = DockStyle.Top, AutoSize = false, Height = 22 };
    _previewBox = new _NearestNeighborPictureBox {
      Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black,
    };
    var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
    rightPanel.Controls.Add(_previewBox);     // fills available space
    rightPanel.Controls.Add(previewLabel);    // dock-top above the picture box

    // SplitContainer: dragable boundary between left controls and right preview. The user can
    // pull the splitter to grow either side; the preview's Zoom mode auto-rescales the bitmap.
    // NOTE: do NOT set Panel1MinSize / Panel2MinSize in the object initializer — the SplitContainer's
    // default Width is 150, so the (260 + 240) min-sizes would force SplitterDistance out of range
    // and throw InvalidOperationException before the control gets a parent. Defer them to Load
    // (after the form's Dock=Fill has stretched the splitter to the real client width).
    var split = new SplitContainer {
      Dock = DockStyle.Fill,
      Orientation = Orientation.Vertical,
      SplitterWidth = 6,
      FixedPanel = FixedPanel.Panel1,
    };
    split.Panel1.Controls.Add(leftPanel);
    split.Panel2.Controls.Add(rightPanel);
    this.Load += (_, _) => {
      try {
        split.Panel1MinSize = 260;
        split.Panel2MinSize = 240;
        split.SplitterDistance = Math.Min(320, this.ClientSize.Width - split.Panel2MinSize - split.SplitterWidth);
      } catch { /* ignored — splitter clamps itself */ }
    };

    // Bottom button strip — anchored Right so resize keeps OK/Cancel pinned.
    var buttonStrip = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(10, 8, 10, 8) };
    _cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    _okButton = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Width = 80, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    _okButton.Click += (_, _) => this._OnAccept();
    buttonStrip.Resize += (_, _) => {
      _cancelButton.Location = new Point(buttonStrip.ClientSize.Width - _cancelButton.Width - 10, 8);
      _okButton.Location     = new Point(_cancelButton.Left - _okButton.Width - 10, 8);
    };
    buttonStrip.Controls.AddRange([_cancelButton, _okButton]);
    this.AcceptButton = _okButton;
    this.CancelButton = _cancelButton;

    this.Controls.Add(split);
    this.Controls.Add(buttonStrip);

    this._UpdatePreview();
  }

  /// <summary>Pre-select the defaults the caller wants (so re-opening the dialog feels sticky).</summary>
  public void SetDefaults(int columns, int rows, string? codepage = null) {
    if (columns >= _columnsInput.Minimum && columns <= _columnsInput.Maximum) _columnsInput.Value = columns;
    if (rows >= _rowsInput.Minimum && rows <= _rowsInput.Maximum) _rowsInput.Value = rows;
    if (codepage is not null)
      foreach (var item in _codepageCombo.Items)
        if (((string)item).StartsWith(codepage, StringComparison.OrdinalIgnoreCase)) {
          _codepageCombo.SelectedItem = item;
          break;
        }
  }

  private (int W, int H) _CurrentCellSize() {
    var ix = _cellSizeCombo.SelectedIndex;
    if (ix < 0 || ix >= _CellSizes.Length) ix = 2;
    return (_CellSizes[ix].W, _CellSizes[ix].H);
  }

  private BitmapFont? _ResolveSelectedFont() {
    var fontName = _fontCombo.SelectedItem as string ?? _allFontNames[0];

    // Embedded era fonts override the cell-size combo with their natural dimensions.
    if (_embeddedAccessors.TryGetValue(fontName, out var emb))
      return emb.Get();

    var (cellW, cellH) = this._CurrentCellSize();
    var cacheKey = $"{fontName}@{cellW}x{cellH}";
    if (_fontCache.TryGetValue(cacheKey, out var cached)) return cached;

    BitmapFont font;
    try {
      var familyName = fontName.StartsWith(_SystemFontPrefix) ? fontName.Substring(_SystemFontPrefix.Length) : fontName;
      font = BitmapFontRasterizer.FromSystemFont(familyName, cellW, cellH);
    } catch (Exception ex) {
      _statusLabel.Text = $"Failed to rasterise '{fontName}' at {cellW}×{cellH}: {ex.Message}";
      _statusLabel.ForeColor = Color.IndianRed;
      return null;
    }

    _fontCache[cacheKey] = font;
    return font;
  }

  private void _OnAccept() {
    this.PickedFont = this._ResolveSelectedFont() ?? BitmapFontEmbedded.IbmVga8x16;
    this.PickedCodepage = ((string)_codepageCombo.SelectedItem).Split(' ')[0];
    this.PickedColumns = (int)_columnsInput.Value;
    this.PickedRows = (int)_rowsInput.Value;
  }

  private void _OnLoadFontClick(object? sender, EventArgs e) {
    using var dlg = new OpenFileDialog {
      Title = "Load raw 8×N VGA font (.F16 / .F8 / .BIN)",
      Filter = "Font files (*.f16;*.f14;*.f8;*.fon;*.bin)|*.f16;*.f14;*.f8;*.fon;*.bin|All files (*.*)|*.*",
    };
    if (dlg.ShowDialog(this) != DialogResult.OK) return;
    try {
      var bytes = File.ReadAllBytes(dlg.FileName);
      var height = bytes.Length / 256;
      if (height is < 6 or > 32) {
        MessageBox.Show(this, $"Expected 256 glyphs × 6..32 rows (got {bytes.Length} bytes). Not a recognised VGA font binary.", "Font load failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }
      var font = BitmapFont.FromBytes(8, height, bytes);
      var name = Path.GetFileNameWithoutExtension(dlg.FileName) + $" ({bytes.Length}B)";
      var cacheKey = $"{name}@8x{height}";
      _fontCache[cacheKey] = font;
      if (!_fontCombo.Items.Contains(name))
        _fontCombo.Items.Add(name);
      _fontCombo.SelectedItem = name;
      // Snap cell-size combo to whatever the loaded font's height is, if it matches a preset.
      for (var i = 0; i < _CellSizes.Length; ++i)
        if (_CellSizes[i].H == height) { _cellSizeCombo.SelectedIndex = i; break; }
    } catch (Exception ex) {
      MessageBox.Show(this, ex.Message, "Font load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void _UpdatePreview() {
    var font = this._ResolveSelectedFont();
    if (font is null) return;
    _statusLabel.ForeColor = Color.DarkGray;

    // Full 16×16 CP437 character set — codepoint 0x00 (top-left) through 0xFF (bottom-right).
    // Foreground 14 (yellow) on background 1 (blue) makes every glyph visible even when the
    // codepoint is space.
    const int rows = 16;
    const int cols = 16;
    var cells = new TextCell[cols * rows];
    for (var i = 0; i < cells.Length; ++i)
      cells[i] = new TextCell((byte)i, Foreground: 14, Background: 1);

    var screen = new TextScreen { ColumnCount = cols, RowCount = rows, Cells = cells, Font = font };
    var img = TextScreenRenderer.Render(screen, font);
    var srcBmp = _RgbBytesToBitmap(img.Width, img.Height, img.PixelData);

    // Stamp every cell into a destination bitmap with a 1-pixel gap between cells (filled with
    // a desaturated grid colour). This gives clean dividers WITHOUT overdrawing the top scanline
    // of each glyph the way a post-hoc DrawLine pass would.
    const int gap = 1;
    var cellW = font.CellWidth;
    var cellH = font.CellHeight;
    var dstW = cols * cellW + (cols - 1) * gap;
    var dstH = rows * cellH + (rows - 1) * gap;
    var dstBmp = new Bitmap(dstW, dstH, PixelFormat.Format24bppRgb);
    using (var g = Graphics.FromImage(dstBmp)) {
      g.Clear(Color.FromArgb(60, 80, 120));
      g.InterpolationMode = InterpolationMode.NearestNeighbor;
      g.PixelOffsetMode = PixelOffsetMode.Half;
      for (var r = 0; r < rows; ++r)
        for (var c = 0; c < cols; ++c) {
          var srcRect = new Rectangle(c * cellW, r * cellH, cellW, cellH);
          var dstRect = new Rectangle(c * (cellW + gap), r * (cellH + gap), cellW, cellH);
          g.DrawImage(srcBmp, dstRect, srcRect, GraphicsUnit.Pixel);
        }
    }
    srcBmp.Dispose();

    _previewBox.Image?.Dispose();
    _previewBox.Image = dstBmp;
  }

  private static Bitmap _RgbBytesToBitmap(int width, int height, byte[] rgb) {
    var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
    var rect = new Rectangle(0, 0, width, height);
    var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
    try {
      var rowStride = data.Stride;
      var line = new byte[rowStride];
      for (var y = 0; y < height; ++y) {
        for (var x = 0; x < width; ++x) {
          var srcOff = (y * width + x) * 3;
          // GDI+ Bitmap is BGR, our buffer is RGB — swap.
          line[x * 3]     = rgb[srcOff + 2];
          line[x * 3 + 1] = rgb[srcOff + 1];
          line[x * 3 + 2] = rgb[srcOff];
        }
        System.Runtime.InteropServices.Marshal.Copy(line, 0, data.Scan0 + y * rowStride, rowStride);
      }
    } finally {
      bmp.UnlockBits(data);
    }
    return bmp;
  }
}
