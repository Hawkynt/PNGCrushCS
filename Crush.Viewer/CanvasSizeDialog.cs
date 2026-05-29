using System;
using System.Drawing;
using System.Windows.Forms;
using Optimizer.Image;

namespace Crush.Viewer;

/// <summary>
/// Dialog that asks the user for new canvas dimensions, anchor position, and fill color.
/// Used by the "Canvas Size..." menu item.
/// </summary>
internal sealed class CanvasSizeDialog : Form {

  public int TargetWidth { get; private set; }
  public int TargetHeight { get; private set; }
  public new AnchorPosition Anchor { get; private set; } = AnchorPosition.Center;
  public Color FillColor { get; private set; } = Color.White;

  private readonly int _sourceWidth;
  private readonly int _sourceHeight;

  private readonly NumericUpDown _widthUpDown;
  private readonly NumericUpDown _heightUpDown;
  private readonly RadioButton[,] _anchorGrid = new RadioButton[3, 3];
  private readonly Panel _colorSwatch;
  private readonly Label _previewLabel;

  public CanvasSizeDialog(int sourceWidth, int sourceHeight) {
    this._sourceWidth = sourceWidth;
    this._sourceHeight = sourceHeight;

    this.Text = "Canvas Size";
    this.Size = new(380, 360);
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;
    this.StartPosition = FormStartPosition.CenterParent;

    var y = 12;

    // Source dimensions
    var sourceLabel = new Label { Text = $"Current size: {sourceWidth} x {sourceHeight}", Location = new(12, y), AutoSize = true };
    this.Controls.Add(sourceLabel);
    y += 28;

    // New Width
    var widthLabel = new Label { Text = "New Width:", Location = new(12, y + 2), AutoSize = true };
    this._widthUpDown = new() { Location = new(120, y), Width = 90, Minimum = 1, Maximum = 16384, Value = sourceWidth };
    this.Controls.Add(widthLabel);
    this.Controls.Add(this._widthUpDown);
    y += 28;

    // New Height
    var heightLabel = new Label { Text = "New Height:", Location = new(12, y + 2), AutoSize = true };
    this._heightUpDown = new() { Location = new(120, y), Width = 90, Minimum = 1, Maximum = 16384, Value = sourceHeight };
    this.Controls.Add(heightLabel);
    this.Controls.Add(this._heightUpDown);
    y += 32;

    // Anchor position label
    var anchorLabel = new Label { Text = "Anchor:", Location = new(12, y), AutoSize = true };
    this.Controls.Add(anchorLabel);
    y += 20;

    // 3x3 radio button grid
    var anchorPanel = new Panel { Location = new(12, y), Size = new(200, 90) };
    var names = new[,] {
      { "TL", "TC", "TR" },
      { "ML", "C",  "MR" },
      { "BL", "BC", "BR" },
    };
    for (var row = 0; row < 3; ++row) {
      for (var col = 0; col < 3; ++col) {
        var rb = new RadioButton {
          Text = names[row, col],
          Location = new(col * 66, row * 28),
          Size = new(60, 24),
          Appearance = Appearance.Button,
          TextAlign = ContentAlignment.MiddleCenter,
          FlatStyle = FlatStyle.Flat,
        };
        if (row == 1 && col == 1) rb.Checked = true; // Center default
        rb.CheckedChanged += (_, _) => this._UpdatePreview();
        this._anchorGrid[row, col] = rb;
        anchorPanel.Controls.Add(rb);
      }
    }

    this.Controls.Add(anchorPanel);
    y += 96;

    // Fill color
    var colorLabel = new Label { Text = "Fill Color:", Location = new(12, y + 2), AutoSize = true };
    this._colorSwatch = new() { Location = new(120, y), Size = new(24, 24), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    var colorBtn = new Button { Text = "Choose...", Location = new(150, y), Size = new(80, 24) };
    colorBtn.Click += this._OnChooseColor;
    this.Controls.Add(colorLabel);
    this.Controls.Add(this._colorSwatch);
    this.Controls.Add(colorBtn);
    y += 32;

    // Preview
    this._previewLabel = new() { Text = "", Location = new(12, y), Size = new(340, 20), ForeColor = SystemColors.GrayText };
    this.Controls.Add(this._previewLabel);
    y += 24;

    // OK / Cancel
    var okBtn = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new(190, y), Size = new(80, 28) };
    var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new(276, y), Size = new(80, 28) };
    this.Controls.Add(okBtn);
    this.Controls.Add(cancelBtn);
    this.AcceptButton = okBtn;
    this.CancelButton = cancelBtn;

    // Wire events
    this._widthUpDown.ValueChanged += (_, _) => this._UpdatePreview();
    this._heightUpDown.ValueChanged += (_, _) => this._UpdatePreview();
    okBtn.Click += this._OnOk;

    this._UpdatePreview();
  }

  private AnchorPosition _GetSelectedAnchor() {
    for (var row = 0; row < 3; ++row)
      for (var col = 0; col < 3; ++col)
        if (this._anchorGrid[row, col].Checked)
          return (AnchorPosition)(row * 3 + col);
    return AnchorPosition.Center;
  }

  private void _OnChooseColor(object? sender, EventArgs e) {
    using var dlg = new ColorDialog { Color = this.FillColor };
    if (dlg.ShowDialog(this) == DialogResult.OK) {
      this.FillColor = dlg.Color;
      this._colorSwatch.BackColor = dlg.Color;
      this._UpdatePreview();
    }
  }

  private void _UpdatePreview() {
    var w = (int)this._widthUpDown.Value;
    var h = (int)this._heightUpDown.Value;
    var anchor = this._GetSelectedAnchor();
    var (ox, oy) = ImageTransformer._ComputeAnchorOffset(this._sourceWidth, this._sourceHeight, w, h, anchor);
    this._previewLabel.Text = $"{this._sourceWidth}x{this._sourceHeight} -> {w}x{h}, source placed at ({ox}, {oy})";
  }

  private void _OnOk(object? sender, EventArgs e) {
    this.TargetWidth = (int)this._widthUpDown.Value;
    this.TargetHeight = (int)this._heightUpDown.Value;
    this.Anchor = this._GetSelectedAnchor();
  }
}
