using System;
using System.Collections.Generic;
using System.Drawing;
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

  private const string _BuiltInName = "« Built-in VGA 8×16 »";
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
  private readonly PictureBox _previewBox;
  private readonly Label _statusLabel;

  // Resolved BitmapFont for each (fontName, cellW, cellH) combo. System fonts are rasterised on
  // demand and cached here so re-selecting the same font is instant.
  private readonly Dictionary<string, BitmapFont> _fontCache = new();

  // Names shown in the font combo: built-in first, then alphabetised system font families.
  private readonly string[] _allFontNames;

  /// <summary>The font the user picked (null until OK).</summary>
  public BitmapFont? PickedFont { get; private set; }

  /// <summary>The code page identifier the user picked (e.g. "CP437"). Always set on OK.</summary>
  public string PickedCodepage { get; private set; } = "CP437";

  public int PickedColumns { get; private set; } = 80;
  public int PickedRows { get; private set; } = 25;

  public FontCodepageWindow() {
    this.Text = "Font and code page";
    this.Size = new Size(640, 440);
    this.StartPosition = FormStartPosition.CenterParent;
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;

    _fontCache[_BuiltInName + "@8x16"] = BitmapFont.DefaultVga8x16;

    var systemFamilies = BitmapFontRasterizer.GetInstalledMonospaceFamilies();
    var combined = new List<string>(systemFamilies.Length + 1) { _BuiltInName };
    combined.AddRange(systemFamilies);
    _allFontNames = combined.ToArray();

    var leftPanel = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(10) };
    var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

    var fontLabel = new Label { Text = "Font:", Top = 0, Left = 0, AutoSize = true };
    _fontCombo = new ComboBox {
      Top = 18, Left = 0, Width = 270, DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = false, MaxDropDownItems = 20,
    };
    _fontCombo.Items.AddRange(_allFontNames);
    _fontCombo.SelectedIndex = 0;
    _fontCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();

    _loadFontButton = new Button { Text = "Load .F16…", Top = 47, Left = 0, Width = 120, Height = 23 };
    _loadFontButton.Click += this._OnLoadFontClick;

    var cellSizeLabel = new Label { Text = "Cell size:", Top = 80, Left = 0, AutoSize = true };
    _cellSizeCombo = new ComboBox {
      Top = 98, Left = 0, Width = 270, DropDownStyle = ComboBoxStyle.DropDownList,
    };
    foreach (var (_, _, lbl) in _CellSizes) _cellSizeCombo.Items.Add(lbl);
    _cellSizeCombo.SelectedIndex = 2; // 8x16 default
    _cellSizeCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();

    var codepageLabel = new Label { Text = "Code page:", Top = 130, Left = 0, AutoSize = true };
    _codepageCombo = new ComboBox {
      Top = 148, Left = 0, Width = 270, DropDownStyle = ComboBoxStyle.DropDownList,
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
      Top = 240, Left = 0, Width = 280, Height = 100, AutoSize = false, ForeColor = Color.DarkGray,
      Text = "Built-in is the procedural VGA-style font.\nAny installed system font can also be picked — TrueType\nmonospace fonts (Consolas, Cascadia Mono, Mxoldschool\nPC fonts) work best.",
    };

    leftPanel.Controls.AddRange([fontLabel, _fontCombo, _loadFontButton, cellSizeLabel, _cellSizeCombo, codepageLabel, _codepageCombo, colsLabel, _columnsInput, rowsLabel, _rowsInput, _statusLabel]);

    var previewLabel = new Label { Text = "Preview:", Top = 0, Left = 0, AutoSize = true };
    _previewBox = new PictureBox {
      Top = 18, Left = 0, Width = 300, Height = 320, BorderStyle = BorderStyle.FixedSingle,
      SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black,
    };
    rightPanel.Controls.AddRange([previewLabel, _previewBox]);

    _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Top = 365, Left = 440, Width = 80 };
    _okButton.Click += (_, _) => this._OnAccept();
    _cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Top = 365, Left = 530, Width = 80 };
    this.AcceptButton = _okButton;
    this.CancelButton = _cancelButton;

    this.Controls.AddRange([leftPanel, rightPanel, _okButton, _cancelButton]);

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
    var fontName = _fontCombo.SelectedItem as string ?? _BuiltInName;
    var (cellW, cellH) = this._CurrentCellSize();
    var cacheKey = $"{fontName}@{cellW}x{cellH}";
    if (_fontCache.TryGetValue(cacheKey, out var cached)) return cached;

    // Built-in is only valid at 8×16 (it's procedural at that size). For other cell sizes fall
    // through to system-font rasterisation using "Consolas" as a sensible default.
    BitmapFont font;
    try {
      if (fontName == _BuiltInName && cellW == 8 && cellH == 16) {
        font = BitmapFont.DefaultVga8x16;
      } else {
        var familyName = fontName == _BuiltInName ? "Consolas" : fontName;
        font = BitmapFontRasterizer.FromSystemFont(familyName, cellW, cellH);
      }
    } catch (Exception ex) {
      _statusLabel.Text = $"Failed to rasterise '{fontName}' at {cellW}×{cellH}: {ex.Message}";
      _statusLabel.ForeColor = Color.IndianRed;
      return null;
    }

    _fontCache[cacheKey] = font;
    return font;
  }

  private void _OnAccept() {
    this.PickedFont = this._ResolveSelectedFont() ?? BitmapFont.DefaultVga8x16;
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

    // Render a CP437 sample so the user can see what their picks look like.
    // Sample includes ASCII + box-drawing + shades + blocks — the regions NFO/ANSI exercise most.
    byte[] sample = [
      0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64,
      0,
      0xC9, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xBB, 0x20, 0x20,
      0,
      0xBA, 0x20, 0xB0, 0xB1, 0xB2, 0xDB, 0xDF, 0xDC, 0x20, 0xBA, 0x20,
      0,
      0xC8, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xBC, 0x20, 0x20,
    ];
    var rows = 4;
    var cols = 11;
    var cells = new TextCell[cols * rows];
    var r = 0; var c = 0;
    foreach (var b in sample) {
      if (b == 0) { ++r; c = 0; continue; }
      if (c < cols && r < rows) cells[r * cols + c] = new TextCell(b, Foreground: 14, Background: 1);
      ++c;
    }
    for (var i = 0; i < cells.Length; ++i)
      if (cells[i].CodePoint == 0)
        cells[i] = new TextCell(0x20, 7, 1);

    var screen = new TextScreen { ColumnCount = cols, RowCount = rows, Cells = cells, Font = font };
    var img = TextScreenRenderer.Render(screen, font);
    _previewBox.Image?.Dispose();
    _previewBox.Image = _RgbBytesToBitmap(img.Width, img.Height, img.PixelData);
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
