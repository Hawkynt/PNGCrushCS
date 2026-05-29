using System;
using System.Drawing;
using System.Windows.Forms;
using Optimizer.Image;

namespace Crush.Viewer;

/// <summary>
/// Dialog that asks the user for target crop dimensions, aspect ratio locking,
/// and whether to resize after cropping. Used by the "Crop to Size..." menu item.
/// </summary>
internal sealed class CropToSizeDialog : Form {

  public int TargetWidth { get; private set; }
  public int TargetHeight { get; private set; }
  public bool ResizeAfterCrop { get; private set; }
  public InterpolationHint Interpolation { get; private set; } = InterpolationHint.Bicubic;

  private readonly int _sourceWidth;
  private readonly int _sourceHeight;

  private readonly NumericUpDown _widthUpDown;
  private readonly NumericUpDown _heightUpDown;
  private readonly CheckBox _resizeCheck;
  private readonly ComboBox _interpCombo;
  private readonly Label _previewLabel;

  public CropToSizeDialog(int sourceWidth, int sourceHeight) {
    this._sourceWidth = sourceWidth;
    this._sourceHeight = sourceHeight;

    this.Text = "Crop to Size";
    this.ClientSize = new(360, 290);
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;
    this.StartPosition = FormStartPosition.CenterParent;

    var y = 12;

    var sourceLabel = new Label { Text = $"Source: {sourceWidth} x {sourceHeight}", Location = new(12, y), AutoSize = true };
    this.Controls.Add(sourceLabel);
    y += 28;

    var widthLabel = new Label { Text = "Target Width:", Location = new(12, y + 2), AutoSize = true };
    this._widthUpDown = new() { Location = new(130, y), Width = 90, Minimum = 1, Maximum = 16384, Value = sourceWidth };
    this.Controls.Add(widthLabel);
    this.Controls.Add(this._widthUpDown);
    y += 28;

    var heightLabel = new Label { Text = "Target Height:", Location = new(12, y + 2), AutoSize = true };
    this._heightUpDown = new() { Location = new(130, y), Width = 90, Minimum = 1, Maximum = 16384, Value = sourceHeight };
    this.Controls.Add(heightLabel);
    this.Controls.Add(this._heightUpDown);
    y += 28;

    var infoLabel = new Label {
      Text = "The crop rectangle will be locked to this aspect ratio.\nDrag to position, then press Enter to apply.",
      Location = new(12, y),
      Size = new(320, 36),
      ForeColor = SystemColors.GrayText,
    };
    this.Controls.Add(infoLabel);
    y += 40;

    this._resizeCheck = new() { Text = "Resize to target dimensions after cropping", Location = new(12, y), AutoSize = true, Checked = true };
    this.Controls.Add(this._resizeCheck);
    y += 28;

    var interpLabel = new Label { Text = "Interpolation:", Location = new(12, y + 2), AutoSize = true };
    this._interpCombo = new() { Location = new(130, y), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    this._interpCombo.Items.AddRange(["Nearest Neighbor", "Bilinear", "Bicubic"]);
    this._interpCombo.SelectedIndex = 2;
    this.Controls.Add(interpLabel);
    this.Controls.Add(this._interpCombo);
    y += 32;

    this._previewLabel = new() { Text = "", Location = new(12, y), Size = new(320, 20), ForeColor = SystemColors.GrayText };
    this.Controls.Add(this._previewLabel);
    y += 24;

    var okBtn = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new(194, y), Size = new(75, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new(275, y), Size = new(75, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    this.Controls.Add(okBtn);
    this.Controls.Add(cancelBtn);
    this.AcceptButton = okBtn;
    this.CancelButton = cancelBtn;

    this._widthUpDown.ValueChanged += (_, _) => this._UpdatePreview();
    this._heightUpDown.ValueChanged += (_, _) => this._UpdatePreview();
    this._resizeCheck.CheckedChanged += (_, _) => {
      this._interpCombo.Enabled = this._resizeCheck.Checked;
      this._UpdatePreview();
    };

    okBtn.Click += this._OnOk;
    this._UpdatePreview();
  }

  private void _UpdatePreview() {
    var tw = (int)this._widthUpDown.Value;
    var th = (int)this._heightUpDown.Value;
    var aspect = tw / (float)th;
    var suffix = this._resizeCheck.Checked ? $" then resize to {tw}x{th}" : "";
    this._previewLabel.Text = $"Aspect ratio: {aspect:F3}{suffix}";
  }

  private void _OnOk(object? sender, EventArgs e) {
    this.TargetWidth = (int)this._widthUpDown.Value;
    this.TargetHeight = (int)this._heightUpDown.Value;
    this.ResizeAfterCrop = this._resizeCheck.Checked;
    this.Interpolation = this._interpCombo.SelectedIndex switch {
      0 => InterpolationHint.NearestNeighbor,
      1 => InterpolationHint.Bilinear,
      2 => InterpolationHint.Bicubic,
      _ => InterpolationHint.Bicubic,
    };
  }
}
