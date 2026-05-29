using System;
using System.Drawing;
using System.Windows.Forms;

namespace Crush.Viewer;

/// <summary>
/// Dialog that asks the user for a desired aspect ratio before entering interactive crop mode.
/// Provides common presets and a custom W:H input.
/// </summary>
internal sealed class AspectRatioCropDialog : Form {

  /// <summary>The selected aspect ratio (width / height).</summary>
  public float AspectRatio { get; private set; } = 1f;

  private readonly ComboBox _presetCombo;
  private readonly Label _customLabel;
  private readonly NumericUpDown _customW;
  private readonly Label _colonLabel;
  private readonly NumericUpDown _customH;
  private readonly Label _previewLabel;

  public AspectRatioCropDialog() {
    this.Text = "Crop with Aspect Ratio";
    this.ClientSize = new(340, 220);
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;
    this.StartPosition = FormStartPosition.CenterParent;

    var y = 12;

    var label = new Label { Text = "Aspect Ratio:", Location = new(12, y + 2), AutoSize = true };
    this._presetCombo = new() { Location = new(120, y), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    this._presetCombo.Items.AddRange(["1:1 (Square)", "4:3", "3:2", "16:9", "16:10", "Custom..."]);
    this._presetCombo.SelectedIndex = 0;
    this.Controls.Add(label);
    this.Controls.Add(this._presetCombo);
    y += 32;

    // Custom ratio controls (hidden by default)
    this._customLabel = new() { Text = "Ratio:", Location = new(12, y + 2), AutoSize = true, Visible = false };
    this._customW = new() { Location = new(120, y), Width = 60, Minimum = 1, Maximum = 9999, Value = 4, Visible = false };
    this._colonLabel = new() { Text = ":", Location = new(184, y + 2), AutoSize = true, Visible = false };
    this._customH = new() { Location = new(200, y), Width = 60, Minimum = 1, Maximum = 9999, Value = 3, Visible = false };
    this.Controls.Add(this._customLabel);
    this.Controls.Add(this._customW);
    this.Controls.Add(this._colonLabel);
    this.Controls.Add(this._customH);
    y += 32;

    // Preview
    this._previewLabel = new() { Text = "", Location = new(12, y), Size = new(300, 20), ForeColor = SystemColors.GrayText };
    this.Controls.Add(this._previewLabel);
    y += 28;

    var infoLabel = new Label {
      Text = "The crop rectangle will be locked to this aspect ratio.\nDrag to position, then press Enter to apply.",
      Location = new(12, y),
      Size = new(300, 36),
      ForeColor = SystemColors.GrayText,
    };
    this.Controls.Add(infoLabel);
    y += 40;

    // OK / Cancel
    var okBtn = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new(174, y), Size = new(75, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new(255, y), Size = new(75, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    this.Controls.Add(okBtn);
    this.Controls.Add(cancelBtn);
    this.AcceptButton = okBtn;
    this.CancelButton = cancelBtn;

    // Wire events
    this._presetCombo.SelectedIndexChanged += this._OnPresetChanged;
    this._customW.ValueChanged += (_, _) => this._UpdatePreview();
    this._customH.ValueChanged += (_, _) => this._UpdatePreview();
    okBtn.Click += this._OnOk;

    this._UpdatePreview();
  }

  private void _OnPresetChanged(object? sender, EventArgs e) {
    var isCustom = this._presetCombo.SelectedIndex == 5;
    this._customLabel.Visible = isCustom;
    this._customW.Visible = isCustom;
    this._colonLabel.Visible = isCustom;
    this._customH.Visible = isCustom;
    this._UpdatePreview();
  }

  private void _UpdatePreview() {
    var ratio = this._GetRatio();
    this._previewLabel.Text = $"Aspect ratio: {ratio:F4}";
  }

  private float _GetRatio() => this._presetCombo.SelectedIndex switch {
    0 => 1f,                                         // 1:1
    1 => 4f / 3f,                                    // 4:3
    2 => 3f / 2f,                                    // 3:2
    3 => 16f / 9f,                                   // 16:9
    4 => 16f / 10f,                                  // 16:10
    5 => (float)this._customW.Value / (float)this._customH.Value, // Custom
    _ => 1f,
  };

  private void _OnOk(object? sender, EventArgs e) {
    this.AspectRatio = this._GetRatio();
  }
}
