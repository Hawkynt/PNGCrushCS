using System;
using System.Drawing;
using System.Windows.Forms;
using FileFormat.Core;
using Optimizer.Image;

namespace Crush.Viewer;

internal sealed class ResizeDialog : Form {

  public int TargetWidth { get; private set; }
  public int TargetHeight { get; private set; }
  public ResizeMode Mode { get; private set; }
  public InterpolationHint Interpolation { get; private set; }
  public Color LetterboxColor { get; private set; } = Color.Black;

  // Crop region (only used when Mode == CropRegion)
  public int CropX { get; private set; }
  public int CropY { get; private set; }
  public int CropWidth { get; private set; }
  public int CropHeight { get; private set; }

  private readonly int _sourceWidth;
  private readonly int _sourceHeight;
  private bool _lockAspect = true;
  private bool _updatingFromLock;

  // Target-format dimension constraint (null = no constraint, free editing).
  private readonly (IntegerRange Width, IntegerRange Height)[]? _allowedDimensions;
  private readonly ComboBox? _presetCombo;
  private bool _suppressDimensionSnap;

  private readonly NumericUpDown _widthUpDown;
  private readonly NumericUpDown _heightUpDown;
  private readonly CheckBox _lockAspectCheck;
  private readonly ComboBox _modeCombo;
  private readonly ComboBox _interpCombo;
  private readonly Button _letterboxBtn;
  private readonly Panel _letterboxSwatch;
  private readonly Label _previewLabel;

  // Crop region controls
  private readonly Label _cropXLabel;
  private readonly NumericUpDown _cropXUpDown;
  private readonly Label _cropYLabel;
  private readonly NumericUpDown _cropYUpDown;
  private readonly Label _cropWLabel;
  private readonly NumericUpDown _cropWUpDown;
  private readonly Label _cropHLabel;
  private readonly NumericUpDown _cropHUpDown;
  private readonly Button _selectOnImageBtn;

  public ResizeDialog(int sourceWidth, int sourceHeight, InterpolationHint defaultHint,
      (IntegerRange Width, IntegerRange Height)[]? allowedDimensions = null) {
    this._sourceWidth = sourceWidth;
    this._sourceHeight = sourceHeight;
    this._allowedDimensions = allowedDimensions is { Length: > 0 } ? allowedDimensions : null;

    this.Text = "Resize Image";
    this.Size = new(400, this._allowedDimensions is { Length: > 1 } ? 412 : 380);
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;
    this.StartPosition = FormStartPosition.CenterParent;

    var y = 12;

    // Source label
    var sourceLabel = new Label { Text = $"Source: {sourceWidth} x {sourceHeight}", Location = new(12, y), AutoSize = true };
    this.Controls.Add(sourceLabel);
    y += 28;

    // Preset dropdown — only shown when the format offers multiple allowed dimension options.
    if (this._allowedDimensions is { Length: > 1 } presets) {
      var presetLabel = new Label { Text = "Target Preset:", Location = new(12, y + 2), AutoSize = true };
      this._presetCombo = new() { Location = new(130, y), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
      foreach (var (w, h) in presets)
        this._presetCombo.Items.Add(_FormatDimension(w, h));
      this._presetCombo.SelectedIndexChanged += this._OnPresetChanged;
      this.Controls.Add(presetLabel);
      this.Controls.Add(this._presetCombo);
      y += 28;
    }

    // Target Width
    var widthLabel = new Label { Text = "Target Width:", Location = new(12, y + 2), AutoSize = true };
    this._widthUpDown = new() { Location = new(130, y), Width = 90, Minimum = 1, Maximum = 16384, Value = sourceWidth };
    this.Controls.Add(widthLabel);
    this.Controls.Add(this._widthUpDown);
    y += 28;

    // Target Height
    var heightLabel = new Label { Text = "Target Height:", Location = new(12, y + 2), AutoSize = true };
    this._heightUpDown = new() { Location = new(130, y), Width = 90, Minimum = 1, Maximum = 16384, Value = sourceHeight };
    this.Controls.Add(heightLabel);
    this.Controls.Add(this._heightUpDown);
    y += 28;

    // Lock aspect ratio
    this._lockAspectCheck = new() { Text = "Lock aspect ratio", Location = new(12, y), Checked = true, AutoSize = true };
    this.Controls.Add(this._lockAspectCheck);
    y += 28;

    // Mode
    var modeLabel = new Label { Text = "Mode:", Location = new(12, y + 2), AutoSize = true };
    this._modeCombo = new() { Location = new(130, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    this._modeCombo.Items.AddRange(["Stretch", "Fit (letterbox)", "Fill (crop)", "Crop region"]);
    this._modeCombo.SelectedIndex = 0;
    this.Controls.Add(modeLabel);
    this.Controls.Add(this._modeCombo);
    y += 28;

    // Interpolation
    var interpLabel = new Label { Text = "Interpolation:", Location = new(12, y + 2), AutoSize = true };
    this._interpCombo = new() { Location = new(130, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    this._interpCombo.Items.AddRange(["Nearest Neighbor", "Bilinear", "Bicubic"]);
    this._interpCombo.SelectedIndex = defaultHint switch {
      InterpolationHint.NearestNeighbor => 0,
      InterpolationHint.Bilinear => 1,
      InterpolationHint.Bicubic => 2,
      _ => 2,
    };
    this.Controls.Add(interpLabel);
    this.Controls.Add(this._interpCombo);
    y += 28;

    // Letterbox color
    var letterboxLabel = new Label { Text = "Letterbox color:", Location = new(12, y + 2), AutoSize = true };
    this._letterboxSwatch = new() { Location = new(130, y), Size = new(24, 24), BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
    this._letterboxBtn = new() { Text = "Choose...", Location = new(160, y), Size = new(80, 24), Enabled = false };
    this.Controls.Add(letterboxLabel);
    this.Controls.Add(this._letterboxSwatch);
    this.Controls.Add(this._letterboxBtn);
    y += 32;

    // Crop region controls (hidden by default)
    this._cropXLabel = new() { Text = "Crop X:", Location = new(12, y + 2), AutoSize = true, Visible = false };
    this._cropXUpDown = new() { Location = new(130, y), Width = 70, Minimum = 0, Maximum = 16384, Value = 0, Visible = false };
    this._cropYLabel = new() { Text = "Y:", Location = new(210, y + 2), AutoSize = true, Visible = false };
    this._cropYUpDown = new() { Location = new(230, y), Width = 70, Minimum = 0, Maximum = 16384, Value = 0, Visible = false };
    this.Controls.Add(this._cropXLabel);
    this.Controls.Add(this._cropXUpDown);
    this.Controls.Add(this._cropYLabel);
    this.Controls.Add(this._cropYUpDown);
    y += 28;

    this._cropWLabel = new() { Text = "Crop W:", Location = new(12, y + 2), AutoSize = true, Visible = false };
    this._cropWUpDown = new() { Location = new(130, y), Width = 70, Minimum = 1, Maximum = 16384, Value = sourceWidth, Visible = false };
    this._cropHLabel = new() { Text = "H:", Location = new(210, y + 2), AutoSize = true, Visible = false };
    this._cropHUpDown = new() { Location = new(230, y), Width = 70, Minimum = 1, Maximum = 16384, Value = sourceHeight, Visible = false };
    this.Controls.Add(this._cropWLabel);
    this.Controls.Add(this._cropWUpDown);
    this.Controls.Add(this._cropHLabel);
    this.Controls.Add(this._cropHUpDown);
    y += 28;

    // "Select on Image" button (visible only in crop mode)
    this._selectOnImageBtn = new() { Text = "Select on Image...", Location = new(12, y), Size = new(140, 28), Visible = false };
    this._selectOnImageBtn.Click += this._OnSelectOnImage;
    this.Controls.Add(this._selectOnImageBtn);

    // Preview label (placed near bottom)
    var bottomOffset = this._presetCombo != null ? 28 : 0;
    this._previewLabel = new() { Text = "", Location = new(12, 268 + bottomOffset), Size = new(360, 20), ForeColor = SystemColors.GrayText };
    this.Controls.Add(this._previewLabel);

    // OK / Cancel buttons
    var okBtn = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new(210, 298 + bottomOffset), Size = new(80, 28) };
    var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new(296, 298 + bottomOffset), Size = new(80, 28) };
    this.Controls.Add(okBtn);
    this.Controls.Add(cancelBtn);
    this.AcceptButton = okBtn;
    this.CancelButton = cancelBtn;

    // Wire events
    this._widthUpDown.ValueChanged += this._OnWidthChanged;
    this._heightUpDown.ValueChanged += this._OnHeightChanged;
    this._lockAspectCheck.CheckedChanged += (_, _) => this._lockAspect = this._lockAspectCheck.Checked;
    this._modeCombo.SelectedIndexChanged += this._OnModeChanged;
    this._interpCombo.SelectedIndexChanged += (_, _) => this._UpdatePreview();
    this._letterboxBtn.Click += this._OnChooseLetterboxColor;
    this._cropXUpDown.ValueChanged += (_, _) => this._UpdatePreview();
    this._cropYUpDown.ValueChanged += (_, _) => this._UpdatePreview();
    this._cropWUpDown.ValueChanged += (_, _) => this._UpdatePreview();
    this._cropHUpDown.ValueChanged += (_, _) => this._UpdatePreview();

    okBtn.Click += this._OnOk;

    // Apply the dimension constraint (if any): pick the closest preset and snap the spinners.
    if (this._allowedDimensions is { } dims) {
      var bestIdx = _PickClosestEntry(dims, sourceWidth, sourceHeight);
      if (this._presetCombo != null) this._presetCombo.SelectedIndex = bestIdx;
      this._ApplyAllowedDimensionsForEntry(bestIdx);
    }

    this._UpdatePreview();
  }

  /// <summary>Formats an allowed-dimension entry as a human-readable label like "280x192" or "128 × 8..8192 step 8".</summary>
  private static string _FormatDimension(IntegerRange w, IntegerRange h) {
    return $"{_FormatAxis(w)} × {_FormatAxis(h)}";
  }

  private static string _FormatAxis(IntegerRange r) {
    if (r.IsFixed) return r.Min.ToString();
    if (r.Step > 1) return $"{r.Min}..{r.Max} step {r.Step}";
    return $"{r.Min}..{r.Max}";
  }

  /// <summary>Returns the index of the entry whose centre is closest to the given source dimensions.</summary>
  private static int _PickClosestEntry((IntegerRange Width, IntegerRange Height)[] entries, int srcW, int srcH) {
    var bestIdx = 0;
    var bestDist = double.PositiveInfinity;
    for (var i = 0; i < entries.Length; ++i) {
      var (w, h) = entries[i];
      var cw = (w.Min + w.Max) / 2.0;
      var ch = (h.Min + h.Max) / 2.0;
      var dw = srcW - cw;
      var dh = srcH - ch;
      var d = dw * dw + dh * dh;
      if (d < bestDist) { bestDist = d; bestIdx = i; }
    }
    return bestIdx;
  }

  /// <summary>Applies the selected preset's range to the W/H spinners — clamps Min/Max, snaps current value.</summary>
  private void _ApplyAllowedDimensionsForEntry(int entryIndex) {
    if (this._allowedDimensions is not { } dims || entryIndex < 0 || entryIndex >= dims.Length) return;
    var (w, h) = dims[entryIndex];

    this._suppressDimensionSnap = true;
    try {
      this._widthUpDown.Minimum = w.Min;
      this._widthUpDown.Maximum = w.Max;
      this._widthUpDown.Increment = Math.Max(1, w.Step);
      this._widthUpDown.Value = w.SnapToValid(this._sourceWidth);
      this._widthUpDown.Enabled = !w.IsFixed;

      this._heightUpDown.Minimum = h.Min;
      this._heightUpDown.Maximum = h.Max;
      this._heightUpDown.Increment = Math.Max(1, h.Step);
      this._heightUpDown.Value = h.SnapToValid(this._sourceHeight);
      this._heightUpDown.Enabled = !h.IsFixed;
    } finally {
      this._suppressDimensionSnap = false;
    }
    this._UpdatePreview();
  }

  private void _OnPresetChanged(object? sender, EventArgs e) {
    if (this._presetCombo == null) return;
    this._ApplyAllowedDimensionsForEntry(this._presetCombo.SelectedIndex);
  }

  /// <summary>Snaps the width spinner to the current entry's allowed values. Returns true if the value changed.</summary>
  private bool _SnapWidthToAllowed() {
    if (this._allowedDimensions is not { } dims) return false;
    var idx = this._presetCombo?.SelectedIndex ?? 0;
    if (idx < 0 || idx >= dims.Length) return false;
    var snapped = dims[idx].Width.SnapToValid((int)this._widthUpDown.Value);
    if (snapped == (int)this._widthUpDown.Value) return false;
    this._widthUpDown.Value = snapped;
    return true;
  }

  private bool _SnapHeightToAllowed() {
    if (this._allowedDimensions is not { } dims) return false;
    var idx = this._presetCombo?.SelectedIndex ?? 0;
    if (idx < 0 || idx >= dims.Length) return false;
    var snapped = dims[idx].Height.SnapToValid((int)this._heightUpDown.Value);
    if (snapped == (int)this._heightUpDown.Value) return false;
    this._heightUpDown.Value = snapped;
    return true;
  }

  private void _OnWidthChanged(object? sender, EventArgs e) {
    if (this._updatingFromLock) return;
    if (this._suppressDimensionSnap) { this._UpdatePreview(); return; }

    // Snap to allowed values if a dimension constraint is active. This may fire ValueChanged again.
    if (this._SnapWidthToAllowed()) return;

    if (!this._lockAspect || this._sourceWidth == 0) {
      this._UpdatePreview(); return; }

    this._updatingFromLock = true;
    var newH = Math.Max(1, (int)Math.Round((double)this._widthUpDown.Value * this._sourceHeight / this._sourceWidth));
    var clamped = Math.Clamp(newH, (int)this._heightUpDown.Minimum, (int)this._heightUpDown.Maximum);
    // If a dimension constraint exists, snap to its step as well.
    if (this._allowedDimensions is { } dims && (this._presetCombo?.SelectedIndex ?? 0) is var idx && idx >= 0 && idx < dims.Length)
      clamped = dims[idx].Height.SnapToValid(clamped);
    this._heightUpDown.Value = clamped;
    this._updatingFromLock = false;
    this._UpdatePreview();
  }

  private void _OnHeightChanged(object? sender, EventArgs e) {
    if (this._updatingFromLock) return;
    if (this._suppressDimensionSnap) { this._UpdatePreview(); return; }

    if (this._SnapHeightToAllowed()) return;

    if (!this._lockAspect || this._sourceHeight == 0) {
      this._UpdatePreview(); return; }

    this._updatingFromLock = true;
    var newW = Math.Max(1, (int)Math.Round((double)this._heightUpDown.Value * this._sourceWidth / this._sourceHeight));
    var clamped = Math.Clamp(newW, (int)this._widthUpDown.Minimum, (int)this._widthUpDown.Maximum);
    if (this._allowedDimensions is { } dims && (this._presetCombo?.SelectedIndex ?? 0) is var idx && idx >= 0 && idx < dims.Length)
      clamped = dims[idx].Width.SnapToValid(clamped);
    this._widthUpDown.Value = clamped;
    this._updatingFromLock = false;
    this._UpdatePreview();
  }

  private void _OnModeChanged(object? sender, EventArgs e) {
    var isFit = this._modeCombo.SelectedIndex == 1;
    var isCrop = this._modeCombo.SelectedIndex == 3;
    this._letterboxBtn.Enabled = isFit;

    var showCrop = isCrop;
    this._cropXLabel.Visible = showCrop;
    this._cropXUpDown.Visible = showCrop;
    this._cropYLabel.Visible = showCrop;
    this._cropYUpDown.Visible = showCrop;
    this._cropWLabel.Visible = showCrop;
    this._cropWUpDown.Visible = showCrop;
    this._cropHLabel.Visible = showCrop;
    this._cropHUpDown.Visible = showCrop;
    this._selectOnImageBtn.Visible = showCrop;

    // When switching to crop region, disable target W/H and lock aspect
    this._widthUpDown.Enabled = !isCrop;
    this._heightUpDown.Enabled = !isCrop;
    this._lockAspectCheck.Enabled = !isCrop;
    this._interpCombo.Enabled = !isCrop;

    this._UpdatePreview();
  }

  private void _OnChooseLetterboxColor(object? sender, EventArgs e) {
    using var dlg = new ColorDialog { Color = this.LetterboxColor };
    if (dlg.ShowDialog(this) == DialogResult.OK) {
      this.LetterboxColor = dlg.Color;
      this._letterboxSwatch.BackColor = dlg.Color;
      this._UpdatePreview();
    }
  }

  private void _UpdatePreview() {
    var modeIndex = this._modeCombo.SelectedIndex;
    if (modeIndex == 3) {
      // Crop region mode
      var cx = (int)this._cropXUpDown.Value;
      var cy = (int)this._cropYUpDown.Value;
      var cw = (int)this._cropWUpDown.Value;
      var ch = (int)this._cropHUpDown.Value;
      this._previewLabel.Text = $"{this._sourceWidth}x{this._sourceHeight} -> crop ({cx},{cy}) {cw}x{ch}";
    } else {
      var tw = (int)this._widthUpDown.Value;
      var th = (int)this._heightUpDown.Value;
      var modeName = modeIndex switch { 0 => "stretch", 1 => "fit", 2 => "fill", _ => "" };

      if (modeIndex == 1) {
        // Fit mode — show centered dimensions
        var scale = Math.Min(tw / (float)this._sourceWidth, th / (float)this._sourceHeight);
        var fitW = (int)(this._sourceWidth * scale);
        var fitH = (int)(this._sourceHeight * scale);
        this._previewLabel.Text = $"{this._sourceWidth}x{this._sourceHeight} -> {fitW}x{fitH} centered in {tw}x{th}";
      } else {
        this._previewLabel.Text = $"{this._sourceWidth}x{this._sourceHeight} -> {tw}x{th} ({modeName})";
      }
    }
  }

  private void _OnSelectOnImage(object? sender, EventArgs e) {
    // Populate crop values from current controls so MainForm can use them as initial rect
    this.CropX = (int)this._cropXUpDown.Value;
    this.CropY = (int)this._cropYUpDown.Value;
    this.CropWidth = (int)this._cropWUpDown.Value;
    this.CropHeight = (int)this._cropHUpDown.Value;
    this.DialogResult = DialogResult.Retry;
    this.Close();
  }

  private void _OnOk(object? sender, EventArgs e) {
    var modeIndex = this._modeCombo.SelectedIndex;
    this.Mode = modeIndex switch {
      0 => ResizeMode.Stretch,
      1 => ResizeMode.Fit,
      2 => ResizeMode.Fill,
      3 => ResizeMode.CropRegion,
      _ => ResizeMode.Stretch,
    };

    this.Interpolation = this._interpCombo.SelectedIndex switch {
      0 => InterpolationHint.NearestNeighbor,
      1 => InterpolationHint.Bilinear,
      2 => InterpolationHint.Bicubic,
      _ => InterpolationHint.Bicubic,
    };

    if (this.Mode == ResizeMode.CropRegion) {
      this.CropX = (int)this._cropXUpDown.Value;
      this.CropY = (int)this._cropYUpDown.Value;
      this.CropWidth = (int)this._cropWUpDown.Value;
      this.CropHeight = (int)this._cropHUpDown.Value;
      this.TargetWidth = this.CropWidth;
      this.TargetHeight = this.CropHeight;
    } else {
      this.TargetWidth = (int)this._widthUpDown.Value;
      this.TargetHeight = (int)this._heightUpDown.Value;
    }
  }
}
