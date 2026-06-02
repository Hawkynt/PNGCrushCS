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

  private readonly ComboBox _fontCombo;
  private readonly ComboBox _codepageCombo;
  private readonly NumericUpDown _columnsInput;
  private readonly NumericUpDown _rowsInput;
  private readonly Button _loadFontButton;
  private readonly Button _okButton;
  private readonly Button _cancelButton;
  private readonly PictureBox _previewBox;
  private readonly Label _statusLabel;

  private readonly Dictionary<string, BitmapFont> _fonts = new();

  /// <summary>The font the user picked (null until OK).</summary>
  public BitmapFont? PickedFont { get; private set; }

  /// <summary>The code page identifier the user picked (e.g. "CP437"). Always set on OK.</summary>
  public string PickedCodepage { get; private set; } = "CP437";

  public int PickedColumns { get; private set; } = 80;
  public int PickedRows { get; private set; } = 25;

  public FontCodepageWindow() {
    this.Text = "Font and code page";
    this.Size = new Size(620, 360);
    this.StartPosition = FormStartPosition.CenterParent;
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;

    _fonts["Built-in VGA 8x16"] = BitmapFont.DefaultVga8x16;

    var leftPanel = new Panel { Dock = DockStyle.Left, Width = 280, Padding = new Padding(10) };
    var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

    var fontLabel = new Label { Text = "Font:", Top = 0, Left = 0, AutoSize = true };
    _fontCombo = new ComboBox {
      Top = 18, Left = 0, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList,
    };
    _fontCombo.Items.AddRange([.. _fonts.Keys]);
    _fontCombo.SelectedIndex = 0;
    _fontCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();

    _loadFontButton = new Button { Text = "Load .F16…", Top = 18, Left = 256, Width = 90, Height = 23 };
    _loadFontButton.Click += this._OnLoadFontClick;

    var codepageLabel = new Label { Text = "Code page:", Top = 50, Left = 0, AutoSize = true };
    _codepageCombo = new ComboBox {
      Top = 68, Left = 0, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList,
    };
    _codepageCombo.Items.AddRange(["CP437 (IBM PC original)", "CP850 (Western European)", "CP866 (Cyrillic)"]);
    _codepageCombo.SelectedIndex = 0;
    _codepageCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();

    var colsLabel = new Label { Text = "Columns:", Top = 105, Left = 0, AutoSize = true };
    _columnsInput = new NumericUpDown {
      Top = 123, Left = 0, Width = 80, Minimum = 1, Maximum = 4096, Value = 80,
    };
    _columnsInput.ValueChanged += (_, _) => this._UpdatePreview();

    var rowsLabel = new Label { Text = "Rows:", Top = 105, Left = 100, AutoSize = true };
    _rowsInput = new NumericUpDown {
      Top = 123, Left = 100, Width = 80, Minimum = 1, Maximum = 4096, Value = 25,
    };
    _rowsInput.ValueChanged += (_, _) => this._UpdatePreview();

    _statusLabel = new Label {
      Top = 165, Left = 0, Width = 260, Height = 60, AutoSize = false, ForeColor = Color.DarkGray,
      Text = "Defaults match the classic 80×25 DOS text mode.\nSwap the font for an authentic VGA ROM look.",
    };

    leftPanel.Controls.AddRange([fontLabel, _fontCombo, _loadFontButton, codepageLabel, _codepageCombo, colsLabel, _columnsInput, rowsLabel, _rowsInput, _statusLabel]);

    var previewLabel = new Label { Text = "Preview:", Top = 0, Left = 0, AutoSize = true };
    _previewBox = new PictureBox {
      Top = 18, Left = 0, Width = 280, Height = 240, BorderStyle = BorderStyle.FixedSingle,
      SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black,
    };
    rightPanel.Controls.AddRange([previewLabel, _previewBox]);

    _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Top = 280, Left = 420, Width = 80 };
    _okButton.Click += (_, _) => this._OnAccept();
    _cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Top = 280, Left = 510, Width = 80 };
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

  private void _OnAccept() {
    var fontName = _fontCombo.SelectedItem as string ?? "Built-in VGA 8x16";
    this.PickedFont = _fonts[fontName];
    this.PickedCodepage = ((string)_codepageCombo.SelectedItem).Split(' ')[0];
    this.PickedColumns = (int)_columnsInput.Value;
    this.PickedRows = (int)_rowsInput.Value;
  }

  private void _OnLoadFontClick(object? sender, EventArgs e) {
    using var dlg = new OpenFileDialog {
      Title = "Load raw 8x16 VGA font (.F16)",
      Filter = "Font files (*.f16;*.fon;*.bin)|*.f16;*.fon;*.bin|All files (*.*)|*.*",
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
      _fonts[name] = font;
      _fontCombo.Items.Add(name);
      _fontCombo.SelectedItem = name;
    } catch (Exception ex) {
      MessageBox.Show(this, ex.Message, "Font load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void _UpdatePreview() {
    var fontName = _fontCombo.SelectedItem as string ?? "Built-in VGA 8x16";
    if (!_fonts.TryGetValue(fontName, out var font)) return;

    // Render a CP437 sample so the user can see what their picks look like.
    // Sample includes ASCII + box-drawing + shades + blocks — the regions NFO/ANSI exercise most.
    byte[] sample = [
      // Row 1: lowercase + uppercase
      0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64,
      // Row 2 (separated by 0): box drawing
      0,
      0xC9, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xBB, 0x20, 0x20,
      // Row 3: shades + full block
      0,
      0xBA, 0x20, 0xB0, 0xB1, 0xB2, 0xDB, 0xDF, 0xDC, 0x20, 0xBA, 0x20,
      // Row 4: digits + corner
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
