using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Hawkynt.ColorProcessing;
using Hawkynt.ColorProcessing.Dithering;
using Hawkynt.ColorProcessing.Quantization;

namespace Hawkynt.ImageTransformUI;

/// <summary>Preview display mode for the color reduction dialog.</summary>
internal enum PreviewMode {
  PreviewOnly,
  SideBySide,
  SliderOverlay,
}

/// <summary>
/// Modal dialog for choosing a quantizer, ditherer, and palette size. Shows a live
/// preview thumbnail of the quantized result. Returns the user's choices as string
/// names so the caller can pass them to any dispatch mechanism.
/// </summary>
public sealed class ReduceColorsWindow : Form {

  private readonly Bitmap _source;
  private readonly ListBox _quantizerList;
  private readonly ListBox _dithererList;
  private readonly TrackBar _paletteSlider;
  private readonly Label _paletteLabel;
  private readonly Panel _previewPanel;
  private readonly Label _statusLabel;
  private readonly Button _okButton;
  private readonly Button _cancelButton;

  // Collapsible parameter panels
  private readonly Panel _quantizerParamPanel;
  private readonly Button _quantizerParamToggle;
  private readonly FlowLayoutPanel _quantizerParamContainer;
  private readonly Panel _dithererParamPanel;
  private readonly Button _dithererParamToggle;
  private readonly FlowLayoutPanel _dithererParamContainer;
  private bool _quantizerParamExpanded;
  private bool _dithererParamExpanded;
  private Dictionary<string, object?> _quantizerParamValues = new();
  private Dictionary<string, object?> _dithererParamValues = new();
  private _ParameterInfo[] _currentQuantizerParams = [];
  private _ParameterInfo[] _currentDithererParams = [];

  private CancellationTokenSource? _previewCts;
  private System.Windows.Forms.Timer? _previewDebounce;

  private List<QuantizerDescriptor> _quantizers = null!;
  private List<DithererDescriptor> _ditherers = null!;

  // Palette-size constraint (null = unrestricted; non-null = sorted disjoint ranges).
  private (int Min, int Max)[]? _allowedRanges;
  private bool _suppressSliderSnap;

  // Fixed-palette mode (set via SetFixedPalettes): when active, quantizer UI is hidden and a
  // palette dropdown is shown instead. The ditherer remains user-selectable.
  private (string Name, byte[] Rgb)[]? _fixedPalettes;
  private bool _useFixedPalette;
  private readonly Label _fixedPaletteLabel;
  private readonly ComboBox _fixedPaletteCombo;
  private readonly Panel _topSectionHost;
  private readonly TableLayoutPanel _quantizerSection;
  private readonly TableLayoutPanel _fixedSection;

  // Subset-picker state — used when a format's master palette (e.g. NES 64) exceeds
  // the per-image colour limit declared in AllowedPaletteRanges (e.g. 4). The user
  // picks which N master entries are active; auto-pick chooses the N best for the image.
  private readonly Label _subsetCountLabel;
  private readonly Button _autoPickButton;
  private readonly _SwatchPickerPanel _subsetSwatches;
  private readonly Panel _subsetPickerPanel;
  // Per-palette selection state, indexed by master-palette entry. true = active.
  private bool[][]? _subsetSelections;
  // True when the currently chosen master palette has more entries than the per-image colour limit.
  private bool _useSubsetPicker;

  // Preview mode state
  private PreviewMode _previewMode = PreviewMode.PreviewOnly;
  private Bitmap? _originalThumb;
  private Bitmap? _quantizedThumb;
  private float _sliderPosition = 0.5f; // 0..1, default center
  private bool _draggingSlider;

  // Zoom/pan state
  private float _previewZoom = 1f;
  private PointF _previewOffset;
  private bool _previewPanning;
  private Point _previewLastMouse;
  private bool _previewAutoFit = true;
  private const float _ZOOM_MIN = 0.1f;
  private const float _ZOOM_MAX = 20f;
  private const float _ZOOM_FACTOR = 1.2f;
  private const int _SLIDER_HIT_TOLERANCE = 8;

  /// <summary>The name of the picked quantizer, or null if the dialog was cancelled.</summary>
  public string? PickedQuantizerName { get; private set; }

  /// <summary>The name of the picked ditherer, or null if none selected.</summary>
  public string? PickedDithererName { get; private set; }

  /// <summary>The chosen palette size (2..256).</summary>
  public int PaletteSize { get; private set; } = 256;

  /// <summary>The quantizer parameter overrides chosen by the user, or null if defaults.</summary>
  public Dictionary<string, object?>? PickedQuantizerParams { get; private set; }

  /// <summary>The ditherer parameter overrides chosen by the user, or null if defaults.</summary>
  public Dictionary<string, object?>? PickedDithererParams { get; private set; }

  /// <summary>When fixed-palette mode is active, the name of the chosen palette (else null).</summary>
  public string? PickedFixedPaletteName { get; private set; }

  /// <summary>When fixed-palette mode is active, the packed-RGB colour data of the chosen palette (else null).</summary>
  public byte[]? PickedFixedPaletteColors { get; private set; }

  /// <summary>Initializes a new instance of this type.</summary>
  public ReduceColorsWindow(Bitmap source) {
    _source = source ?? throw new ArgumentNullException(nameof(source));

    Text = "Reduce Colours";
    Size = new Size(1240, 680);
    MinimumSize = new Size(1000, 560);
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowIcon = false;
    FormBorderStyle = FormBorderStyle.Sizable;

    // --- Layout: left column (controls) | right column (preview) ---
    // Note: order matters — SplitterDistance/MinSize validation reads against the SplitContainer's current
    // Width. An unparented SplitContainer defaults to ~150px wide, so we set an explicit Size first;
    // Dock=Fill takes over once it's added to the form. Otherwise we'd hit InvalidOperationException
    // ("SplitterDistance muss zwischen Panel1MinSize und Width - Panel2MinSize liegen").
    var splitContainer = new SplitContainer {
      Size = new Size(1200, 580),
      Orientation = Orientation.Vertical,
      FixedPanel = FixedPanel.Panel1,
      SplitterWidth = 6,
    };
    splitContainer.Panel1MinSize = 560;
    splitContainer.Panel2MinSize = 320;
    splitContainer.SplitterDistance = 640;
    splitContainer.Dock = DockStyle.Fill;

    // Left panel layout — the top section hosts EITHER the palette-size+quantizer UI
    // OR the fixed-palette dropdown (mutually exclusive).
    var leftPanel = new TableLayoutPanel {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 6,
      Padding = new Padding(6),
    };
    leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // 0: top section (quantizer or fixed palette)
    leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 1: dither label
    leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // 2: dither list
    leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 3: dither params
    leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 4: status
    leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 5: buttons

    _paletteLabel = new Label { Text = "Palette size: 256 colours", Dock = DockStyle.Top, AutoSize = true };
    _paletteSlider = new TrackBar { Dock = DockStyle.Top, Minimum = 2, Maximum = 256, Value = 256, TickFrequency = 16, LargeChange = 16, SmallChange = 2 };
    _paletteSlider.ValueChanged += _OnPaletteSliderValueChanged;

    var quantLabel = new Label { Text = "Quantizer:", Dock = DockStyle.Top, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0) };
    _quantizerList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
    _quantizerList.SelectedIndexChanged += _OnQuantizerSelectionChanged;

    // Quantizer parameter panel (collapsible)
    _quantizerParamToggle = new Button {
      Text = "Parameters ▶",
      Dock = DockStyle.Top,
      FlatStyle = FlatStyle.Flat,
      TextAlign = ContentAlignment.MiddleLeft,
      Height = 22,
      Cursor = Cursors.Hand,
      ForeColor = Color.DimGray,
      Padding = new Padding(0),
      Margin = new Padding(0),
    };
    _quantizerParamToggle.FlatAppearance.BorderSize = 0;
    _quantizerParamToggle.Click += (_, _) => _ToggleParamPanel(ref _quantizerParamExpanded, _quantizerParamToggle, _quantizerParamContainer!);

    _quantizerParamContainer = new FlowLayoutPanel {
      Dock = DockStyle.Top,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Visible = false,
      Padding = new Padding(8, 2, 2, 2),
    };

    _quantizerParamPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    _quantizerParamPanel.Controls.Add(_quantizerParamContainer);
    _quantizerParamPanel.Controls.Add(_quantizerParamToggle);

    // Ditherer parameter panel (collapsible)
    var ditherLabel = new Label { Text = "Ditherer:", Dock = DockStyle.Top, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0) };
    _dithererList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
    _dithererList.SelectedIndexChanged += _OnDithererSelectionChanged;

    _dithererParamToggle = new Button {
      Text = "Parameters ▶",
      Dock = DockStyle.Top,
      FlatStyle = FlatStyle.Flat,
      TextAlign = ContentAlignment.MiddleLeft,
      Height = 22,
      Cursor = Cursors.Hand,
      ForeColor = Color.DimGray,
      Padding = new Padding(0),
      Margin = new Padding(0),
    };
    _dithererParamToggle.FlatAppearance.BorderSize = 0;
    _dithererParamToggle.Click += (_, _) => _ToggleParamPanel(ref _dithererParamExpanded, _dithererParamToggle, _dithererParamContainer!);

    _dithererParamContainer = new FlowLayoutPanel {
      Dock = DockStyle.Top,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Visible = false,
      Padding = new Padding(8, 2, 2, 2),
    };

    _dithererParamPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    _dithererParamPanel.Controls.Add(_dithererParamContainer);
    _dithererParamPanel.Controls.Add(_dithererParamToggle);

    _statusLabel = new Label { Text = "Pick a quantizer and ditherer.", Dock = DockStyle.Top, AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 4, 0, 0) };

    var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    _cancelButton = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
    _okButton = new Button { Text = "OK", Width = 80, Enabled = false };
    _okButton.Click += _OnOkClicked;
    buttonPanel.Controls.Add(_cancelButton);
    buttonPanel.Controls.Add(_okButton);

    // --- Top section: quantizer mode (palette slider + quantizer list + quant params) ---
    _quantizerSection = new TableLayoutPanel {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 5,
    };
    _quantizerSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _quantizerSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 0: palette label
    _quantizerSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 1: palette slider
    _quantizerSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 2: quant label
    _quantizerSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));// 3: quant list (fills)
    _quantizerSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 4: quant params
    _quantizerSection.Controls.Add(_paletteLabel, 0, 0);
    _quantizerSection.Controls.Add(_paletteSlider, 0, 1);
    _quantizerSection.Controls.Add(quantLabel, 0, 2);
    _quantizerSection.Controls.Add(_quantizerList, 0, 3);
    _quantizerSection.Controls.Add(_quantizerParamPanel, 0, 4);

    // --- Top section: fixed-palette mode (label + owner-drawn combo of palettes) ---
    _fixedPaletteLabel = new Label {
      Text = "Choose palette:",
      Dock = DockStyle.Top,
      AutoSize = true,
      Font = new Font(Font, FontStyle.Bold),
      Padding = new Padding(0, 4, 0, 4),
    };
    _fixedPaletteCombo = new ComboBox {
      Dock = DockStyle.Top,
      DropDownStyle = ComboBoxStyle.DropDownList,
      DrawMode = DrawMode.OwnerDrawFixed,
      ItemHeight = 24,
    };
    _fixedPaletteCombo.DrawItem += _OnFixedPaletteDrawItem;
    _fixedPaletteCombo.SelectedIndexChanged += (_, _) => _OnFixedPaletteSelectionChanged();

    // Subset picker controls — visible only when the master palette exceeds the per-image colour limit.
    var subsetHeader = new FlowLayoutPanel {
      Dock = DockStyle.Top,
      FlowDirection = FlowDirection.LeftToRight,
      AutoSize = true,
      WrapContents = false,
      Padding = new Padding(0, 4, 0, 2),
    };
    _subsetCountLabel = new Label {
      Text = "Selected: 0 / 0",
      AutoSize = true,
      Padding = new Padding(0, 4, 8, 0),
      ForeColor = Color.DimGray,
    };
    _autoPickButton = new Button {
      Text = "Auto-pick from image",
      AutoSize = true,
      FlatStyle = FlatStyle.System,
    };
    _autoPickButton.Click += (_, _) => _AutoPickSubset();
    subsetHeader.Controls.Add(_subsetCountLabel);
    subsetHeader.Controls.Add(_autoPickButton);

    _subsetSwatches = new _SwatchPickerPanel {
      Dock = DockStyle.Fill,
      MinimumSize = new Size(0, 100),
    };
    _subsetSwatches.SwatchToggled += _OnSubsetSwatchToggled;

    _subsetPickerPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
    _subsetPickerPanel.Controls.Add(_subsetSwatches);
    _subsetPickerPanel.Controls.Add(subsetHeader);

    _fixedSection = new TableLayoutPanel {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3,
      Visible = false, // hidden by default; SetFixedPalettes() toggles
    };
    _fixedSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _fixedSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: label
    _fixedSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: combo
    _fixedSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: subset picker (fills remaining)
    _fixedSection.Controls.Add(_fixedPaletteLabel, 0, 0);
    _fixedSection.Controls.Add(_fixedPaletteCombo, 0, 1);
    _fixedSection.Controls.Add(_subsetPickerPanel, 0, 2);

    // Container hosting both top sub-sections (only one visible at a time).
    _topSectionHost = new Panel { Dock = DockStyle.Fill };
    _topSectionHost.Controls.Add(_quantizerSection);
    _topSectionHost.Controls.Add(_fixedSection);

    leftPanel.Controls.Add(_topSectionHost, 0, 0);
    leftPanel.Controls.Add(ditherLabel, 0, 1);
    leftPanel.Controls.Add(_dithererList, 0, 2);
    leftPanel.Controls.Add(_dithererParamPanel, 0, 3);
    leftPanel.Controls.Add(_statusLabel, 0, 4);
    leftPanel.Controls.Add(buttonPanel, 0, 5);

    // Right panel: preview mode toolbar + custom-painted preview panel
    var rightPanel = new TableLayoutPanel {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3,
      Padding = new Padding(6),
    };
    rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: mode toolbar
    rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: preview label
    rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: preview area

    // Preview mode toolbar
    var modeStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    var btnPreviewOnly = new ToolStripButton("Preview Only") { Checked = true, CheckOnClick = true, Tag = PreviewMode.PreviewOnly };
    var btnSideBySide = new ToolStripButton("Side by Side") { CheckOnClick = true, Tag = PreviewMode.SideBySide };
    var btnSliderOverlay = new ToolStripButton("Slider Overlay") { CheckOnClick = true, Tag = PreviewMode.SliderOverlay };
    ToolStripButton[] modeButtons = [btnPreviewOnly, btnSideBySide, btnSliderOverlay];
    foreach (var btn in modeButtons) {
      btn.Click += (_, _) => {
        _previewMode = (PreviewMode)btn.Tag!;
        foreach (var b in modeButtons) b.Checked = b == btn;
        _previewAutoFit = true;
        _FitPreviewToPanel();
        _previewPanel!.Cursor = Cursors.Default;
        _previewPanel!.Invalidate();
      };
      modeStrip.Items.Add(btn);
    }
    rightPanel.Controls.Add(modeStrip, 0, 0);

    var previewLabel = new Label { Text = "Preview:", Dock = DockStyle.Top, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
    rightPanel.Controls.Add(previewLabel, 0, 1);

    _previewPanel = new _DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 240, 240), BorderStyle = BorderStyle.FixedSingle };
    _previewPanel.Paint += _OnPreviewPaint;
    _previewPanel.MouseDown += _OnPreviewMouseDown;
    _previewPanel.MouseMove += _OnPreviewMouseMove;
    _previewPanel.MouseUp += _OnPreviewMouseUp;
    _previewPanel.MouseWheel += _OnPreviewMouseWheel;
    _previewPanel.MouseDoubleClick += _OnPreviewMouseDoubleClick;
    _previewPanel.Resize += (_, _) => {
      if (_previewAutoFit)
        _FitPreviewToPanel();
      _previewPanel.Invalidate();
    };
    rightPanel.Controls.Add(_previewPanel, 0, 2);

    splitContainer.Panel1.Controls.Add(leftPanel);
    splitContainer.Panel2.Controls.Add(rightPanel);
    Controls.Add(splitContainer);

    AcceptButton = _okButton;
    CancelButton = _cancelButton;

    _previewDebounce = new System.Windows.Forms.Timer { Interval = 300 };
    _previewDebounce.Tick += _OnPreviewDebounceTick;

    _PopulateLists();
    _SetOriginalPreview();
  }

  /// <summary>Constrains the dialog to choose from a fixed set of pre-defined palettes (e.g. CGA palettes, DOOM palette).
  /// Hides the quantizer UI and shows an owner-drawn dropdown of palettes with colour swatches.
  /// The ditherer remains user-selectable. Pass <c>null</c> or empty to restore normal quantizer mode.
  /// <para/>
  /// If any palette has more entries than the current <see cref="SetAllowedPaletteRanges"/> upper bound,
  /// a subset-picker grid is shown so the user can pick which N entries are active (auto-picked from the
  /// source image initially, manual override via swatch clicks). Call <see cref="SetAllowedPaletteRanges"/>
  /// BEFORE this method when both are needed.</summary>
  public void SetFixedPalettes((string Name, byte[] PackedRgb)[]? palettes) {
    if (palettes == null || palettes.Length == 0) {
      _fixedPalettes = null;
      _useFixedPalette = false;
      _fixedSection.Visible = false;
      _quantizerSection.Visible = true;
      _UpdateOkEnabled();
      return;
    }

    // Validate
    foreach (var (name, rgb) in palettes) {
      if (string.IsNullOrEmpty(name)) throw new ArgumentException("Palette name is required.");
      if (rgb == null || rgb.Length == 0 || rgb.Length % 3 != 0)
        throw new ArgumentException($"Palette '{name}' must have a non-empty multiple of 3 bytes (RGB triplets).");
    }

    _fixedPalettes = palettes;
    _useFixedPalette = true;
    _subsetSelections = new bool[palettes.Length][];
    _fixedPaletteCombo.BeginUpdate();
    _fixedPaletteCombo.Items.Clear();
    foreach (var p in palettes)
      _fixedPaletteCombo.Items.Add(p.Name);
    _fixedPaletteCombo.EndUpdate();
    if (_fixedPaletteCombo.Items.Count > 0)
      _fixedPaletteCombo.SelectedIndex = 0;
    _OnFixedPaletteSelectionChanged();

    _quantizerSection.Visible = false;
    _fixedSection.Visible = true;
    _UpdateOkEnabled();
  }

  /// <summary>Returns the configured per-image colour limit, or int.MaxValue if no constraint applies.</summary>
  private int _CurrentMaxAllowed() {
    if (_allowedRanges == null || _allowedRanges.Length == 0) return int.MaxValue;
    return _allowedRanges[_allowedRanges.Length - 1].Max;
  }

  /// <summary>Re-evaluates the subset picker visibility and resets the swatch state for the selected palette.</summary>
  private void _OnFixedPaletteSelectionChanged() {
    if (_fixedPalettes == null || _fixedPaletteCombo.SelectedIndex < 0) return;
    var idx = _fixedPaletteCombo.SelectedIndex;
    var (_, rgb) = _fixedPalettes[idx];
    var count = rgb.Length / 3;
    var maxAllowed = _CurrentMaxAllowed();

    if (count > maxAllowed) {
      _useSubsetPicker = true;
      _subsetPickerPanel.Visible = true;
      // Lazily initialise the per-palette selection (default: auto-pick)
      if (_subsetSelections![idx] == null || _subsetSelections[idx].Length != count) {
        _subsetSelections[idx] = new bool[count];
        _AutoPickSubsetFor(idx, rgb, maxAllowed);
      }
      _subsetSwatches.SetPalette(rgb, _subsetSelections[idx]);
      _UpdateSubsetCountLabel();
    } else {
      _useSubsetPicker = false;
      _subsetPickerPanel.Visible = false;
    }
    _UpdateOkEnabled();
    _OnSettingChanged(this, EventArgs.Empty);
  }

  /// <summary>Picks the <paramref name="targetCount"/> master entries closest to the most-prominent colours
  /// in the source image and marks them as selected.</summary>
  private void _AutoPickSubsetFor(int paletteIdx, byte[] masterRgb, int targetCount) {
    var sel = _subsetSelections![paletteIdx];
    Array.Clear(sel, 0, sel.Length);
    var masterCount = masterRgb.Length / 3;
    if (targetCount >= masterCount) {
      for (var i = 0; i < masterCount; ++i) sel[i] = true;
      return;
    }

    // Sample the source image at a coarse stride to get a colour distribution.
    var samples = _SampleSourceColors(maxSamples: 4096);
    if (samples.Count == 0) {
      // Fall back to evenly-spaced selection
      for (var i = 0; i < targetCount; ++i) sel[i * masterCount / targetCount] = true;
      return;
    }

    // For each master colour, compute the sum of inverse-distances to image samples (= how relevant it is).
    var scores = new double[masterCount];
    for (var i = 0; i < masterCount; ++i) {
      var mr = masterRgb[i * 3];
      var mg = masterRgb[i * 3 + 1];
      var mb = masterRgb[i * 3 + 2];
      double sum = 0;
      foreach (var (sr, sg, sb) in samples) {
        var dr = mr - sr;
        var dg = mg - sg;
        var db = mb - sb;
        var d2 = dr * dr + dg * dg + db * db;
        sum += 1.0 / (1.0 + d2);
      }
      scores[i] = sum;
    }

    // Take the top-N highest-scoring master entries.
    var ranked = Enumerable.Range(0, masterCount).OrderByDescending(i => scores[i]).Take(targetCount).ToArray();
    foreach (var i in ranked) sel[i] = true;
  }

  /// <summary>Samples the source bitmap at a stride for the auto-pick algorithm. Returns approximately
  /// <paramref name="maxSamples"/> (R, G, B) tuples.</summary>
  private List<(byte R, byte G, byte B)> _SampleSourceColors(int maxSamples) {
    var result = new List<(byte, byte, byte)>(maxSamples);
    var w = _source.Width;
    var h = _source.Height;
    var total = (long)w * h;
    var stride = (int)Math.Max(1, total / maxSamples);
    var i = 0;
    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x) {
        if (i++ % stride != 0) continue;
        var px = _source.GetPixel(x, y);
        if (px.A < 16) continue; // ignore transparent pixels
        result.Add((px.R, px.G, px.B));
        if (result.Count >= maxSamples) return result;
      }
    }
    return result;
  }

  private void _AutoPickSubset() {
    if (_fixedPalettes == null || _fixedPaletteCombo.SelectedIndex < 0 || _subsetSelections == null) return;
    var idx = _fixedPaletteCombo.SelectedIndex;
    var (_, rgb) = _fixedPalettes[idx];
    _AutoPickSubsetFor(idx, rgb, _CurrentMaxAllowed());
    _subsetSwatches.RefreshSelection();
    _UpdateSubsetCountLabel();
    _OnSettingChanged(this, EventArgs.Empty);
  }

  private void _OnSubsetSwatchToggled(int swatchIndex) {
    _UpdateSubsetCountLabel();
    _OnSettingChanged(this, EventArgs.Empty);
  }

  private void _UpdateSubsetCountLabel() {
    if (_fixedPalettes == null || _subsetSelections == null || _fixedPaletteCombo.SelectedIndex < 0) return;
    var sel = _subsetSelections[_fixedPaletteCombo.SelectedIndex];
    var count = sel?.Count(b => b) ?? 0;
    var maxAllowed = _CurrentMaxAllowed();
    var minAllowed = _allowedRanges?[0].Min ?? 0;
    var inRange = count >= minAllowed && count <= maxAllowed;
    _subsetCountLabel.Text = $"Selected: {count} / {maxAllowed}" + (inRange ? "" : "  ⚠");
    _subsetCountLabel.ForeColor = inRange ? Color.DimGray : Color.Firebrick;
  }

  /// <summary>Returns the active palette for OK/preview — either the full master (no subset mode)
  /// or the selected subset.</summary>
  private byte[]? _ActiveFixedPaletteRgb() {
    if (_fixedPalettes == null || _fixedPaletteCombo.SelectedIndex < 0) return null;
    var idx = _fixedPaletteCombo.SelectedIndex;
    var (_, rgb) = _fixedPalettes[idx];
    if (!_useSubsetPicker || _subsetSelections == null) return rgb;
    var sel = _subsetSelections[idx];
    if (sel == null) return rgb;
    var count = sel.Count(b => b);
    if (count == 0) return null;
    var subset = new byte[count * 3];
    var w = 0;
    for (var i = 0; i < sel.Length; ++i) {
      if (!sel[i]) continue;
      subset[w * 3] = rgb[i * 3];
      subset[w * 3 + 1] = rgb[i * 3 + 1];
      subset[w * 3 + 2] = rgb[i * 3 + 2];
      ++w;
    }
    return subset;
  }

  private void _OnFixedPaletteDrawItem(object? sender, DrawItemEventArgs e) {
    e.DrawBackground();
    if (e.Index < 0 || _fixedPalettes == null || e.Index >= _fixedPalettes.Length) {
      e.DrawFocusRectangle();
      return;
    }
    var (name, rgb) = _fixedPalettes[e.Index];
    var count = rgb.Length / 3;

    // Left: name text, taking ~40% of width (capped).
    var textWidth = Math.Min(e.Bounds.Width / 2, 140);
    var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, textWidth, e.Bounds.Height);
    TextRenderer.DrawText(e.Graphics, name, e.Font ?? Font, textRect, e.ForeColor,
      TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);

    // Right: colour swatches.
    var swatchAreaX = e.Bounds.X + textWidth + 8;
    var swatchAreaWidth = e.Bounds.Right - swatchAreaX - 4;
    if (swatchAreaWidth < count) return; // not enough room for even 1px per swatch
    var swatchWidth = Math.Max(1, Math.Min(swatchAreaWidth / count, 16));
    var swatchHeight = Math.Max(8, e.Bounds.Height - 4);
    var swatchY = e.Bounds.Y + (e.Bounds.Height - swatchHeight) / 2;

    for (var i = 0; i < count; ++i) {
      var x = swatchAreaX + i * swatchWidth;
      if (x + swatchWidth > e.Bounds.Right - 2) break;
      using var br = new SolidBrush(Color.FromArgb(255, rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]));
      e.Graphics.FillRectangle(br, x, swatchY, Math.Max(1, swatchWidth - 1), swatchHeight);
    }
    e.DrawFocusRectangle();
  }

  private void _UpdateOkEnabled() {
    if (!_useFixedPalette) {
      _okButton.Enabled = _quantizerList.SelectedIndex >= 0 && _dithererList.SelectedIndex >= 0;
      return;
    }
    if (_fixedPaletteCombo.SelectedIndex < 0 || _dithererList.SelectedIndex < 0) {
      _okButton.Enabled = false;
      return;
    }
    // In subset-picker mode, require selection count within the allowed range.
    if (_useSubsetPicker && _subsetSelections != null) {
      var sel = _subsetSelections[_fixedPaletteCombo.SelectedIndex];
      var count = sel?.Count(b => b) ?? 0;
      var maxAllowed = _CurrentMaxAllowed();
      var minAllowed = _allowedRanges?[0].Min ?? 2;
      _okButton.Enabled = count >= minAllowed && count <= maxAllowed;
      return;
    }
    _okButton.Enabled = true;
  }

  /// <summary>Forces the palette size to a specific value and disables the slider. Useful for monochrome-only formats.</summary>
  public void ForcePaletteSize(int size) => SetAllowedPaletteRanges([(size, size)]);

  /// <summary>Caps the slider's upper limit (and resets the current value to that limit).
  /// Useful when the target format supports fewer than 256 colours (e.g. 4-bit indexed = 16).</summary>
  public void SetMaxPaletteSize(int max) => SetAllowedPaletteRanges([(2, Math.Max(2, max))]);

  /// <summary>Constrains the palette-size slider to the given disjoint ranges (inclusive).
  /// Examples: <c>[(2,2)]</c> = exactly 2; <c>[(2,256)]</c> = up to 256; <c>[(2,2),(16,16),(256,256)]</c> = discrete; <c>[(16,32),(64,96)]</c> = two ranges.
  /// Pass <c>null</c> or empty to reset to the default 2..256 range.</summary>
  public void SetAllowedPaletteRanges((int Min, int Max)[]? ranges) {
    if (ranges == null || ranges.Length == 0) {
      _allowedRanges = null;
      _paletteSlider.Minimum = 2;
      _paletteSlider.Maximum = 256;
      _paletteSlider.Value = 256;
      _paletteSlider.Enabled = true;
      _paletteSlider.TickStyle = TickStyle.None;
      _paletteSlider.TickFrequency = 16;
      _ApplyNativeTicks();
      _UpdatePaletteLabel();
      return;
    }

    // Sort + validate (non-overlapping, min <= max within each range).
    var sorted = ranges.OrderBy(r => r.Min).ToArray();
    for (var i = 0; i < sorted.Length; ++i) {
      var (mn, mx) = sorted[i];
      if (mn < 1 || mx < mn) throw new ArgumentException("Invalid palette range: " + mn + ".." + mx);
      if (i > 0 && mn <= sorted[i - 1].Max) throw new ArgumentException("Overlapping palette ranges.");
    }

    _allowedRanges = sorted;
    var minAllowed = sorted[0].Min;
    var maxAllowed = sorted[sorted.Length - 1].Max;

    _suppressSliderSnap = true;
    try {
      _paletteSlider.Minimum = minAllowed;
      _paletteSlider.Maximum = Math.Max(maxAllowed, minAllowed);
      _paletteSlider.Value = maxAllowed; // default to largest allowed
      // Slider stays enabled (snap takes care of unreachable positions); only truly fixed (single point) disables.
      _paletteSlider.Enabled = !(sorted.Length == 1 && sorted[0].Min == sorted[0].Max);
      _paletteSlider.TickStyle = TickStyle.BottomRight;
      _paletteSlider.TickFrequency = 1; // we set ticks explicitly; suppress the default uniform ticks by setting them via native call below
    } finally {
      _suppressSliderSnap = false;
    }
    _ApplyNativeTicks();
    _OnPaletteSliderValueChanged(this, EventArgs.Empty);
  }

  private void _OnPaletteSliderValueChanged(object? sender, EventArgs e) {
    if (!_suppressSliderSnap && _allowedRanges != null) {
      var snapped = _SnapToAllowed(_paletteSlider.Value);
      if (snapped != _paletteSlider.Value) {
        _suppressSliderSnap = true;
        try { _paletteSlider.Value = snapped; } finally { _suppressSliderSnap = false; }
        return; // _paletteSlider.Value setter will re-enter this handler
      }
    }
    _UpdatePaletteLabel();
    _OnSettingChanged(sender, e);
  }

  private int _SnapToAllowed(int value) {
    var ranges = _allowedRanges;
    if (ranges == null || ranges.Length == 0) return value;

    // Inside any range -> keep value as-is.
    foreach (var (mn, mx) in ranges)
      if (value >= mn && value <= mx) return value;

    // Otherwise snap to the nearest range boundary.
    var best = ranges[0].Min;
    var bestDist = Math.Abs(value - best);
    foreach (var (mn, mx) in ranges) {
      var d = Math.Abs(value - mn);
      if (d < bestDist) { best = mn; bestDist = d; }
      d = Math.Abs(value - mx);
      if (d < bestDist) { best = mx; bestDist = d; }
    }
    return best;
  }

  private void _UpdatePaletteLabel() {
    var current = _paletteSlider.Value;
    if (_allowedRanges == null || _allowedRanges.Length == 0) {
      _paletteLabel.Text = $"Palette size: {current} colours";
      return;
    }
    var ranges = _allowedRanges;
    // Format the constraint description.
    var parts = new List<string>(ranges.Length);
    foreach (var (mn, mx) in ranges)
      parts.Add(mn == mx ? mn.ToString() : $"{mn}–{mx}");
    var desc = string.Join(", ", parts);
    var isFixed = ranges.Length == 1 && ranges[0].Min == ranges[0].Max;
    var suffix = isFixed ? "fixed" : $"allowed: {desc}";
    _paletteLabel.Text = $"Palette size: {current} colours ({suffix})";
  }

  // --- Native TrackBar tick markers ---
  // The WinForms TrackBar only supports uniform TickFrequency; for non-uniform ticks
  // (e.g. allowed values [2, 16, 256]) we drive the underlying Win32 control directly.

  private const uint _TBM_CLEARTICS = 0x0409;
  private const uint _TBM_SETTIC = 0x0404;

  [DllImport("user32.dll", CharSet = CharSet.Auto)]
  private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

  private void _ApplyNativeTicks() {
    if (!_paletteSlider.IsHandleCreated) {
      // Defer until handle is available
      _paletteSlider.HandleCreated += _OnSliderHandleCreated;
      return;
    }

    // Clear any prior ticks and (re-)install the ones we want.
    SendMessage(_paletteSlider.Handle, _TBM_CLEARTICS, IntPtr.Zero, IntPtr.Zero);

    if (_allowedRanges == null) return;

    var seen = new HashSet<int>();
    foreach (var (mn, mx) in _allowedRanges) {
      if (seen.Add(mn))
        SendMessage(_paletteSlider.Handle, _TBM_SETTIC, IntPtr.Zero, (IntPtr)mn);
      if (mx != mn && seen.Add(mx))
        SendMessage(_paletteSlider.Handle, _TBM_SETTIC, IntPtr.Zero, (IntPtr)mx);
    }
  }

  private void _OnSliderHandleCreated(object? sender, EventArgs e) {
    _paletteSlider.HandleCreated -= _OnSliderHandleCreated;
    _ApplyNativeTicks();
  }

  private void _PopulateLists() {
    _quantizers = QuantizerRegistry.All
      .Where(q => !q.DeclaringType.ContainsGenericParameters)
      .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();

    _ditherers = DithererRegistry.All
      .Where(d => !d.DeclaringType.ContainsGenericParameters)
      .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();

    foreach (var q in _quantizers)
      _quantizerList.Items.Add(q.Name);

    foreach (var d in _ditherers)
      _dithererList.Items.Add(d.Name);

    // Pre-select reasonable defaults
    _SelectByName(_quantizerList, "Median Cut");
    _SelectByName(_dithererList, "ErrorDiffusion_FloydSteinberg");
  }

  private static void _SelectByName(ListBox list, string name) {
    for (var i = 0; i < list.Items.Count; ++i) {
      if (string.Equals(list.Items[i]?.ToString(), name, StringComparison.OrdinalIgnoreCase)) {
        list.SelectedIndex = i;
        return;
      }
    }
    if (list.Items.Count > 0)
      list.SelectedIndex = 0;
  }

  // --- Parameter panel logic ---

  /// <summary>Describes a constructor parameter discovered via reflection.</summary>
  private sealed record _ParameterInfo(string Name, string DisplayName, Type Type, object? DefaultValue, string? Description);

  /// <summary>Discovers constructor parameters for a type, returning non-trivial constructors' params.</summary>
  private static _ParameterInfo[] _DiscoverParameters(Type declaringType) {
    // Find the constructor with the most parameters (primary constructor for record structs)
    var ctors = declaringType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
    if (ctors.Length == 0) return [];

    var bestCtor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
    var parameters = bestCtor.GetParameters();
    if (parameters.Length == 0) return [];

    var result = new List<_ParameterInfo>();
    foreach (var p in parameters) {
      // Skip parameters that are other quantizer/ditherer types (wrapper inner types)
      if (typeof(IQuantizer).IsAssignableFrom(p.ParameterType) || typeof(IDitherer).IsAssignableFrom(p.ParameterType))
        continue;

      var displayName = _PascalToSpaced(p.Name ?? "unknown");
      var defaultValue = p.HasDefaultValue ? p.DefaultValue : _GetTypeDefault(p.ParameterType);
      result.Add(new _ParameterInfo(p.Name ?? "unknown", displayName, p.ParameterType, defaultValue, null));
    }

    return result.ToArray();
  }

  private static string _PascalToSpaced(string name) {
    if (string.IsNullOrEmpty(name)) return name;
    var chars = new List<char> { char.ToUpper(name[0]) };
    for (var i = 1; i < name.Length; ++i) {
      if (char.IsUpper(name[i]) && i > 0 && char.IsLower(name[i - 1]))
        chars.Add(' ');
      chars.Add(name[i]);
    }
    return new string(chars.ToArray());
  }

  private static object? _GetTypeDefault(Type type) {
    if (type == typeof(int)) return 0;
    if (type == typeof(float)) return 0f;
    if (type == typeof(double)) return 0.0;
    if (type == typeof(bool)) return false;
    if (type.IsValueType) return Activator.CreateInstance(type);
    return null;
  }

  private void _ToggleParamPanel(ref bool expanded, Button toggle, FlowLayoutPanel container) {
    expanded = !expanded;
    container.Visible = expanded;
    toggle.Text = expanded ? "Parameters ▼" : "Parameters ▶";
  }

  private void _PopulateParamControls(FlowLayoutPanel container, _ParameterInfo[] parameters, Dictionary<string, object?> values, Button toggle) {
    container.SuspendLayout();
    container.Controls.Clear();
    values.Clear();

    if (parameters.Length == 0) {
      var noParams = new Label {
        Text = "No configurable parameters",
        ForeColor = Color.Gray,
        Font = new Font(Font, FontStyle.Italic),
        AutoSize = true,
        Padding = new Padding(2),
      };
      container.Controls.Add(noParams);
      toggle.ForeColor = Color.LightGray;
    } else {
      toggle.ForeColor = Color.DimGray;
      foreach (var param in parameters) {
        values[param.Name] = param.DefaultValue;
        var row = new FlowLayoutPanel {
          FlowDirection = FlowDirection.LeftToRight,
          AutoSize = true,
          AutoSizeMode = AutoSizeMode.GrowAndShrink,
          WrapContents = false,
          Margin = new Padding(0, 1, 0, 1),
        };

        var label = new Label {
          Text = param.DisplayName + ":",
          AutoSize = true,
          TextAlign = ContentAlignment.MiddleLeft,
          Padding = new Padding(0, 4, 4, 0),
        };
        row.Controls.Add(label);

        Control editor;
        if (param.Type == typeof(bool)) {
          var cb = new CheckBox { Checked = param.DefaultValue is true, AutoSize = true };
          var capturedParam = param;
          cb.CheckedChanged += (_, _) => {
            values[capturedParam.Name] = cb.Checked;
            _OnSettingChanged(cb, EventArgs.Empty);
          };
          editor = cb;
        } else if (param.Type == typeof(int)) {
          var nud = new NumericUpDown {
            Width = 80,
            Minimum = int.MinValue,
            Maximum = int.MaxValue,
            Value = param.DefaultValue is int iv ? iv : 0,
            DecimalPlaces = 0,
          };
          var capturedParam = param;
          nud.ValueChanged += (_, _) => {
            values[capturedParam.Name] = (int)nud.Value;
            _OnSettingChanged(nud, EventArgs.Empty);
          };
          editor = nud;
        } else if (param.Type == typeof(float)) {
          var nud = new NumericUpDown {
            Width = 80,
            Minimum = -999999,
            Maximum = 999999,
            DecimalPlaces = 3,
            Increment = 0.1m,
            Value = param.DefaultValue is float fv ? (decimal)fv : 0m,
          };
          var capturedParam = param;
          nud.ValueChanged += (_, _) => {
            values[capturedParam.Name] = (float)nud.Value;
            _OnSettingChanged(nud, EventArgs.Empty);
          };
          editor = nud;
        } else if (param.Type == typeof(double)) {
          var nud = new NumericUpDown {
            Width = 80,
            Minimum = -999999,
            Maximum = 999999,
            DecimalPlaces = 4,
            Increment = 0.01m,
            Value = param.DefaultValue is double dv ? (decimal)dv : 0m,
          };
          var capturedParam = param;
          nud.ValueChanged += (_, _) => {
            values[capturedParam.Name] = (double)nud.Value;
            _OnSettingChanged(nud, EventArgs.Empty);
          };
          editor = nud;
        } else if (param.Type.IsEnum) {
          var cb = new ComboBox {
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList,
          };
          foreach (var val in Enum.GetValues(param.Type))
            cb.Items.Add(val);
          if (param.DefaultValue != null)
            cb.SelectedItem = param.DefaultValue;
          else if (cb.Items.Count > 0)
            cb.SelectedIndex = 0;
          var capturedParam = param;
          cb.SelectedIndexChanged += (_, _) => {
            values[capturedParam.Name] = cb.SelectedItem;
            _OnSettingChanged(cb, EventArgs.Empty);
          };
          editor = cb;
        } else if (Nullable.GetUnderlyingType(param.Type) is { } underlying && underlying == typeof(int)) {
          // Nullable<int> — use a NumericUpDown with a checkbox to enable/disable
          var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
          var enableCb = new CheckBox { Checked = param.DefaultValue != null, AutoSize = true, Text = "" };
          var nud = new NumericUpDown {
            Width = 70,
            Minimum = int.MinValue,
            Maximum = int.MaxValue,
            Value = param.DefaultValue is int niv ? niv : 0,
            Enabled = param.DefaultValue != null,
          };
          var capturedParam = param;
          enableCb.CheckedChanged += (_, _) => {
            nud.Enabled = enableCb.Checked;
            values[capturedParam.Name] = enableCb.Checked ? (int?)nud.Value : null;
            _OnSettingChanged(enableCb, EventArgs.Empty);
          };
          nud.ValueChanged += (_, _) => {
            if (enableCb.Checked) {
              values[capturedParam.Name] = (int?)nud.Value;
              _OnSettingChanged(nud, EventArgs.Empty);
            }
          };
          panel.Controls.Add(enableCb);
          panel.Controls.Add(nud);
          editor = panel;
        } else {
          // Unsupported type — show read-only label
          editor = new Label {
            Text = param.DefaultValue?.ToString() ?? "(default)",
            AutoSize = true,
            ForeColor = Color.Gray,
            Padding = new Padding(0, 4, 0, 0),
          };
        }

        if (param.Description != null) {
          var tt = new ToolTip();
          tt.SetToolTip(editor, param.Description);
          tt.SetToolTip(label, param.Description);
        }

        row.Controls.Add(editor);
        container.Controls.Add(row);
      }
    }

    container.ResumeLayout(true);
  }

  private void _OnQuantizerSelectionChanged(object? sender, EventArgs e) {
    var qi = _quantizerList.SelectedIndex;
    if (qi >= 0 && qi < _quantizers.Count) {
      var descriptor = _quantizers[qi];
      _currentQuantizerParams = _DiscoverParameters(descriptor.DeclaringType);
      _PopulateParamControls(_quantizerParamContainer, _currentQuantizerParams, _quantizerParamValues, _quantizerParamToggle);
    }
    _OnSettingChanged(sender, e);
  }

  private void _OnDithererSelectionChanged(object? sender, EventArgs e) {
    var di = _dithererList.SelectedIndex;
    if (di >= 0 && di < _ditherers.Count) {
      var descriptor = _ditherers[di];
      _currentDithererParams = _DiscoverParameters(descriptor.DeclaringType);
      _PopulateParamControls(_dithererParamContainer, _currentDithererParams, _dithererParamValues, _dithererParamToggle);
    }
    _OnSettingChanged(sender, e);
  }

  private void _SetOriginalPreview() {
    var old = _originalThumb;
    _originalThumb = _CreateThumbnail(_source);
    old?.Dispose();
    _previewAutoFit = true;
    _FitPreviewToPanel();
    _previewPanel.Invalidate();
  }

  private Bitmap _CreateThumbnail(Bitmap source) {
    const int maxDim = 512;
    var scale = Math.Min((float)maxDim / source.Width, (float)maxDim / source.Height);
    if (scale >= 1f) scale = 1f;
    var w = Math.Max(1, (int)(source.Width * scale));
    var h = Math.Max(1, (int)(source.Height * scale));
    var thumb = new Bitmap(w, h);
    using (var g = Graphics.FromImage(thumb)) {
      g.InterpolationMode = InterpolationMode.HighQualityBicubic;
      g.DrawImage(source, 0, 0, w, h);
    }
    return thumb;
  }

  private void _OnSettingChanged(object? sender, EventArgs e) {
    _UpdateOkEnabled();
    // Debounce preview
    _previewDebounce?.Stop();
    _previewDebounce?.Start();
  }

  private void _OnPreviewDebounceTick(object? sender, EventArgs e) {
    _previewDebounce?.Stop();
    _SchedulePreview();
  }

  private void _SchedulePreview() {
    _previewCts?.Cancel();

    var di = _dithererList.SelectedIndex;
    if (di < 0) return;

    string quantName;
    int paletteSize;
    Dictionary<string, object?>? quantParams;

    if (_useFixedPalette) {
      var pi = _fixedPaletteCombo.SelectedIndex;
      if (pi < 0 || _fixedPalettes == null) return;
      var activeRgb = _ActiveFixedPaletteRgb();
      if (activeRgb == null) return;
      var tuples = _PackedRgbToTuples(activeRgb);
      quantName = "Custom"; // CustomPaletteQuantizer (QuantizationType.Fixed)
      paletteSize = tuples.Length;
      quantParams = new Dictionary<string, object?> { ["palette"] = tuples };
    } else {
      var qi = _quantizerList.SelectedIndex;
      if (qi < 0) return;
      quantName = _quantizers[qi].Name;
      paletteSize = _paletteSlider.Value;
      quantParams = _currentQuantizerParams.Length > 0 ? new Dictionary<string, object?>(_quantizerParamValues) : null;
    }

    var ditherName = _ditherers[di].Name;
    var ditherParams = _currentDithererParams.Length > 0 ? new Dictionary<string, object?>(_dithererParamValues) : null;

    _statusLabel.Text = "Rendering preview...";
    var cts = new CancellationTokenSource();
    _previewCts = cts;

    // Clone the source to avoid GDI+ contention
    Bitmap clone;
    try { clone = (Bitmap)_source.Clone(); } catch { return; }
    var thumb = _CreateThumbnail(clone);
    clone.Dispose();

    Task.Run(() => {
      try {
        if (cts.Token.IsCancellationRequested) { thumb.Dispose(); return; }
        var result = ReduceColorsDispatch.ReduceColors(thumb, quantName, ditherName, paletteSize, true, quantParams, ditherParams);
        thumb.Dispose();
        if (cts.Token.IsCancellationRequested) { result.Dispose(); return; }
        BeginInvoke(() => {
          if (cts.Token.IsCancellationRequested || IsDisposed) { result.Dispose(); return; }
          var old = _quantizedThumb;
          _quantizedThumb = result;
          old?.Dispose();
          _previewAutoFit = true;
          _FitPreviewToPanel();
          _previewPanel.Invalidate();
          _statusLabel.Text = $"Preview: {quantName} + {ditherName} @ {paletteSize} colours";
        });
      } catch (Exception ex) {
        thumb.Dispose();
        try {
          BeginInvoke(() => { _statusLabel.Text = $"Preview failed: {ex.Message}"; });
        } catch { /* form disposed */ }
      }
    }, cts.Token);
  }

  // --- Zoom/pan helpers ---

  /// <summary>Computes zoom and offset to fit the given image dimensions in the panel, centered.</summary>
  private void _FitPreviewToPanel() {
    var img = _originalThumb;
    if (img == null || _previewPanel.ClientSize.Width <= 0 || _previewPanel.ClientSize.Height <= 0) return;

    var panelW = (float)_previewPanel.ClientSize.Width;
    var panelH = (float)_previewPanel.ClientSize.Height;

    // Side-by-side mode: fit to HALF the panel width so each image fills its viewport
    var fitW = _previewMode == PreviewMode.SideBySide ? panelW / 2f - 2f : panelW;

    var scaleX = fitW / img.Width;
    var scaleY = panelH / img.Height;
    _previewZoom = Math.Min(scaleX, scaleY);
    _previewOffset = new PointF(
      (fitW - img.Width * _previewZoom) / 2f,
      (panelH - img.Height * _previewZoom) / 2f
    );
  }

  /// <summary>Returns the destination rectangle for the given image at the current zoom/offset.</summary>
  private RectangleF _ImageDestRect(Bitmap img) => new(_previewOffset.X, _previewOffset.Y, img.Width * _previewZoom, img.Height * _previewZoom);

  /// <summary>Returns the destination rectangle for an image within a viewport at a given X origin.</summary>
  private RectangleF _ImageDestRectInViewport(Bitmap img, float viewportX) =>
    new(viewportX + _previewOffset.X, _previewOffset.Y, img.Width * _previewZoom, img.Height * _previewZoom);

  /// <summary>Configures interpolation for the current zoom level.</summary>
  private void _SetupInterpolation(Graphics g) {
    if (_previewZoom >= 1f) {
      g.InterpolationMode = InterpolationMode.NearestNeighbor;
      g.PixelOffsetMode = PixelOffsetMode.Half;
    } else {
      g.InterpolationMode = InterpolationMode.HighQualityBilinear;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
  }

  /// <summary>Returns the screen X position of the slider divider in overlay mode.</summary>
  private float _GetSliderScreenX(RectangleF dest) => dest.X + dest.Width * _sliderPosition;

  /// <summary>Tests whether a screen X is near the slider divider line.</summary>
  private bool _IsNearSliderDivider(int screenX, RectangleF dest) => Math.Abs(screenX - _GetSliderScreenX(dest)) <= _SLIDER_HIT_TOLERANCE;

  // --- Preview painting ---

  private void _OnPreviewPaint(object? sender, PaintEventArgs e) {
    var g = e.Graphics;
    g.SmoothingMode = SmoothingMode.HighQuality;

    var original = _originalThumb;
    var quantized = _quantizedThumb ?? original;
    if (original == null) return;

    if (_previewAutoFit)
      _FitPreviewToPanel();

    _SetupInterpolation(g);

    switch (_previewMode) {
      case PreviewMode.PreviewOnly:
        _PaintPreviewOnly(g, quantized ?? original);
        break;
      case PreviewMode.SideBySide:
        _PaintSideBySide(g, original, quantized ?? original);
        break;
      case PreviewMode.SliderOverlay:
        _PaintSliderOverlay(g, original, quantized ?? original);
        break;
    }
  }

  private void _PaintPreviewOnly(Graphics g, Bitmap img) {
    var dest = _ImageDestRect(img);
    g.DrawImage(img, dest);
  }

  private void _PaintSideBySide(Graphics g, Bitmap original, Bitmap quantized) {
    var panelW = _previewPanel.ClientSize.Width;
    var panelH = _previewPanel.ClientSize.Height;
    var halfW = panelW / 2 - 1;
    if (halfW < 1) return;

    // Each viewport shows the FULL image at the same zoom/offset, but positioned within its half
    var leftDest = _ImageDestRectInViewport(original, 0f);
    var rightDest = _ImageDestRectInViewport(quantized, halfW + 2f);

    // Left half: original (clipped to left viewport)
    g.SetClip(new RectangleF(0, 0, halfW, panelH));
    _SetupInterpolation(g);
    g.DrawImage(original, leftDest);

    // Right half: quantized (clipped to right viewport)
    g.SetClip(new RectangleF(halfW + 2f, 0, halfW, panelH));
    _SetupInterpolation(g);
    g.DrawImage(quantized, rightDest);

    g.ResetClip();

    // 2px divider at center
    using var divPen = new Pen(Color.FromArgb(180, Color.White), 2f);
    using var divShadow = new Pen(Color.FromArgb(80, Color.Black), 4f);
    var divX = halfW + 1f;
    g.DrawLine(divShadow, divX, 0, divX, panelH);
    g.DrawLine(divPen, divX, 0, divX, panelH);

    // Labels
    using var labelFont = new Font(Font.FontFamily, 8f, FontStyle.Bold);
    using var labelBrush = new SolidBrush(Color.FromArgb(220, Color.White));
    using var bgBrush = new SolidBrush(Color.FromArgb(140, Color.Black));
    _DrawLabel(g, "Original", new Rectangle(4, 4, halfW, panelH), labelFont, labelBrush, bgBrush);
    _DrawLabel(g, "Quantized", new Rectangle(halfW + 6, 4, halfW, panelH), labelFont, labelBrush, bgBrush);
  }

  private static void _DrawLabel(Graphics g, string text, Rectangle imgRect, Font font, Brush textBrush, Brush bgBrush) {
    var size = g.MeasureString(text, font);
    var labelRect = new RectangleF(imgRect.X + 2, imgRect.Y + 2, size.Width + 4, size.Height + 2);
    g.FillRectangle(bgBrush, labelRect);
    g.DrawString(text, font, textBrush, labelRect.X + 2, labelRect.Y + 1);
  }

  private void _PaintSliderOverlay(Graphics g, Bitmap original, Bitmap quantized) {
    var dest = _ImageDestRect(original);

    // Draw original on full area
    g.DrawImage(original, dest);

    // Calculate slider X in screen coordinates (screen-relative, not image-relative)
    var sliderX = _GetSliderScreenX(dest);

    // Clip quantized image to the right side of the slider
    if (sliderX < dest.Right) {
      var clipRect = new RectangleF(sliderX, dest.Y, dest.Right - sliderX, dest.Height);
      var savedClip = g.Clip;
      g.SetClip(clipRect);
      _SetupInterpolation(g);
      g.DrawImage(quantized, dest);
      g.Clip = savedClip;
    }

    // Draw slider divider line
    using var darkPen = new Pen(Color.FromArgb(180, Color.Black), 1f);
    using var lightPen = new Pen(Color.White, 2f);
    g.DrawLine(lightPen, sliderX, dest.Y, sliderX, dest.Bottom);
    g.DrawLine(darkPen, sliderX - 1, dest.Y, sliderX - 1, dest.Bottom);
    g.DrawLine(darkPen, sliderX + 1, dest.Y, sliderX + 1, dest.Bottom);

    // Labels
    using var labelFont = new Font(Font.FontFamily, 8f, FontStyle.Bold);
    using var labelBrush = new SolidBrush(Color.FromArgb(200, Color.Black));
    using var bgBrush = new SolidBrush(Color.FromArgb(160, Color.White));

    if (sliderX > dest.X + 50)
      _DrawLabel(g, "Original", Rectangle.Truncate(new RectangleF(dest.X, dest.Y, sliderX - dest.X, dest.Height)), labelFont, labelBrush, bgBrush);
    if (dest.Right - sliderX > 60)
      _DrawLabel(g, "Quantized", Rectangle.Truncate(new RectangleF(sliderX + 4, dest.Y, dest.Right - sliderX - 4, dest.Height)), labelFont, labelBrush, bgBrush);
  }

  // --- Mouse interaction (zoom/pan + slider) ---

  private void _OnPreviewMouseWheel(object? sender, MouseEventArgs e) {
    var img = _originalThumb;
    if (img == null) return;

    _previewAutoFit = false;

    // Zoom centered on cursor position
    var factor = e.Delta > 0 ? _ZOOM_FACTOR : 1f / _ZOOM_FACTOR;
    var newZoom = Math.Clamp(_previewZoom * factor, _ZOOM_MIN, _ZOOM_MAX);

    // In side-by-side mode, compute image coordinate relative to the half the cursor is in
    if (_previewMode == PreviewMode.SideBySide) {
      var halfW = _previewPanel.ClientSize.Width / 2 - 1;
      var localX = e.X < halfW ? e.X : e.X - (halfW + 2);
      var imageX = (localX - _previewOffset.X) / _previewZoom;
      var imageY = (e.Y - _previewOffset.Y) / _previewZoom;
      _previewZoom = newZoom;
      _previewOffset = new PointF(localX - imageX * _previewZoom, e.Y - imageY * _previewZoom);
    } else {
      // Image coordinate under cursor before zoom
      var imageX = (e.X - _previewOffset.X) / _previewZoom;
      var imageY = (e.Y - _previewOffset.Y) / _previewZoom;
      _previewZoom = newZoom;
      _previewOffset = new PointF(e.X - imageX * _previewZoom, e.Y - imageY * _previewZoom);
    }

    _previewPanel.Invalidate();
  }

  private void _OnPreviewMouseDown(object? sender, MouseEventArgs e) {
    // Give focus to panel so it receives mouse wheel events
    _previewPanel.Focus();

    // Pan: right-click drag only.
    if (e.Button == MouseButtons.Right) {
      _previewPanning = true;
      _previewLastMouse = e.Location;
      _previewPanel.Cursor = Cursors.SizeAll;
      return;
    }

    // Left-click in slider-overlay mode drags the divider when near it.
    if (e.Button == MouseButtons.Left && _previewMode == PreviewMode.SliderOverlay) {
      var img = _originalThumb;
      if (img != null) {
        var dest = _ImageDestRect(img);
        if (_IsNearSliderDivider(e.X, dest)) {
          _draggingSlider = true;
          _UpdateSliderFromMouse(e.X);
        }
      }
    }
  }

  private void _OnPreviewMouseMove(object? sender, MouseEventArgs e) {
    if (_draggingSlider) {
      _UpdateSliderFromMouse(e.X);
      return;
    }

    if (_previewPanning) {
      _previewAutoFit = false;
      _previewOffset = new PointF(
        _previewOffset.X + e.X - _previewLastMouse.X,
        _previewOffset.Y + e.Y - _previewLastMouse.Y
      );
      _previewLastMouse = e.Location;
      _previewPanel.Invalidate();
      return;
    }

    // Hover cursor feedback in slider overlay mode
    if (_previewMode == PreviewMode.SliderOverlay) {
      var img = _originalThumb;
      if (img != null) {
        var dest = _ImageDestRect(img);
        _previewPanel.Cursor = _IsNearSliderDivider(e.X, dest) ? Cursors.SizeWE : Cursors.Default;
        return;
      }
    }
  }

  private void _OnPreviewMouseUp(object? sender, MouseEventArgs e) {
    _draggingSlider = false;
    if (_previewPanning) {
      _previewPanning = false;
      _previewPanel.Cursor = Cursors.Default;
    }
  }

  private void _OnPreviewMouseDoubleClick(object? sender, MouseEventArgs e) {
    if (e.Button != MouseButtons.Left) return;
    _previewAutoFit = true;
    _FitPreviewToPanel();
    _previewPanel.Invalidate();
  }

  private void _UpdateSliderFromMouse(int mouseX) {
    var img = _originalThumb;
    if (img == null) return;
    var dest = _ImageDestRect(img);
    if (dest.Width < 1f) return;
    _sliderPosition = Math.Clamp((mouseX - dest.X) / dest.Width, 0f, 1f);
    _previewPanel.Invalidate();
  }

  private void _OnOkClicked(object? sender, EventArgs e) {
    var di = _dithererList.SelectedIndex;
    if (di < 0) return;

    if (_useFixedPalette) {
      var pi = _fixedPaletteCombo.SelectedIndex;
      if (pi < 0 || _fixedPalettes == null) return;
      var (name, _) = _fixedPalettes[pi];
      var activeRgb = _ActiveFixedPaletteRgb();
      if (activeRgb == null) return;
      PickedFixedPaletteName = name;
      PickedFixedPaletteColors = activeRgb;
      // Also expose via the existing quantizer params slot so dispatch can build the CustomPaletteQuantizer.
      PickedQuantizerName = "Custom";
      PickedQuantizerParams = new Dictionary<string, object?> { ["palette"] = _PackedRgbToTuples(activeRgb) };
      PaletteSize = activeRgb.Length / 3;
    } else {
      var qi = _quantizerList.SelectedIndex;
      if (qi < 0) return;
      PickedQuantizerName = _quantizers[qi].Name;
      PaletteSize = _paletteSlider.Value;
      PickedQuantizerParams = _currentQuantizerParams.Length > 0 ? new Dictionary<string, object?>(_quantizerParamValues) : null;
    }

    PickedDithererName = _ditherers[di].Name;
    PickedDithererParams = _currentDithererParams.Length > 0 ? new Dictionary<string, object?>(_dithererParamValues) : null;
    DialogResult = DialogResult.OK;
    Close();
  }

  /// <summary>Converts packed RGB byte data [R, G, B, R, G, B, ...] to (byte, byte, byte) tuple array
  /// for CustomPaletteQuantizer's <c>palette</c> constructor parameter.</summary>
  private static (byte R, byte G, byte B)[] _PackedRgbToTuples(byte[] packed) {
    var count = packed.Length / 3;
    var result = new (byte, byte, byte)[count];
    for (var i = 0; i < count; ++i)
      result[i] = (packed[i * 3], packed[i * 3 + 1], packed[i * 3 + 2]);
    return result;
  }

  /// <summary>Releases the resources used by this instance.</summary>
  protected override void Dispose(bool disposing) {
    if (disposing) {
      _previewDebounce?.Stop();
      _previewDebounce?.Dispose();
      _previewCts?.Cancel();
      _originalThumb?.Dispose();
      _originalThumb = null;
      _quantizedThumb?.Dispose();
      _quantizedThumb = null;
    }
    base.Dispose(disposing);
  }

  /// <summary>A Panel subclass with double-buffering and selectable style (for mouse wheel focus).</summary>
  private sealed class _DoubleBufferedPanel : Panel {
    internal _DoubleBufferedPanel() {
      SetStyle(
        ControlStyles.OptimizedDoubleBuffer
        | ControlStyles.AllPaintingInWmPaint
        | ControlStyles.UserPaint
        | ControlStyles.ResizeRedraw
        | ControlStyles.Selectable,
        true
      );
    }
  }

  /// <summary>Owner-drawn grid of toggle-able colour swatches, used for subset-picker palettes (e.g. NES master).
  /// Each swatch shows a checkmark overlay when selected. Click to toggle.</summary>
  private sealed class _SwatchPickerPanel : Panel {

    private byte[]? _colors;       // packed RGB [R0,G0,B0,R1,G1,B1,...]
    private bool[]? _selected;     // per-entry toggle
    private const int _SwatchSize = 22;
    private const int _SwatchSpacing = 2;

    internal event Action<int>? SwatchToggled;

    internal _SwatchPickerPanel() {
      SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
        | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
      BackColor = SystemColors.Control;
    }

    /// <summary>Loads a new palette + selection. Lengths must match (selection.Length == colors.Length/3).</summary>
    internal void SetPalette(byte[] colors, bool[] selected) {
      if (colors.Length % 3 != 0) throw new ArgumentException("RGB byte length must be a multiple of 3.");
      if (selected.Length != colors.Length / 3) throw new ArgumentException("Selection length must match palette entry count.");
      _colors = colors;
      _selected = selected;
      Invalidate();
    }

    /// <summary>Updates only the selection array (palette already loaded). Triggers repaint.</summary>
    internal void RefreshSelection() => Invalidate();

    /// <summary>Returns the column count given the current width.</summary>
    private int _ColumnCount() {
      var avail = ClientSize.Width - 2;
      var stride = _SwatchSize + _SwatchSpacing;
      return Math.Max(1, avail / stride);
    }

    /// <summary>Computes the panel height needed to render all swatches at the current width.</summary>
    internal int PreferredHeightFor(int entryCount, int columns) {
      var rows = (entryCount + columns - 1) / columns;
      return rows * (_SwatchSize + _SwatchSpacing) + 4;
    }

    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e);
      if (_colors == null || _selected == null) return;
      var g = e.Graphics;
      var count = _colors.Length / 3;
      var cols = _ColumnCount();
      var stride = _SwatchSize + _SwatchSpacing;

      using var borderPen = new Pen(Color.FromArgb(96, 0, 0, 0), 1f);
      using var selectedBorderPen = new Pen(Color.FromArgb(180, 0, 120, 215), 2f);
      using var checkPen = new Pen(Color.White, 2.5f);
      using var checkShadow = new Pen(Color.FromArgb(150, 0, 0, 0), 3f);

      for (var i = 0; i < count; ++i) {
        var col = i % cols;
        var row = i / cols;
        var x = 2 + col * stride;
        var y = 2 + row * stride;
        var rect = new Rectangle(x, y, _SwatchSize, _SwatchSize);
        using var br = new SolidBrush(Color.FromArgb(255, _colors[i * 3], _colors[i * 3 + 1], _colors[i * 3 + 2]));
        g.FillRectangle(br, rect);
        g.DrawRectangle(_selected[i] ? selectedBorderPen : borderPen, rect);

        if (_selected[i]) {
          // Draw a small checkmark
          var cx = x + 4;
          var cy = y + _SwatchSize / 2;
          PointF p1 = new(cx, cy);
          PointF p2 = new(cx + 5, cy + 5);
          PointF p3 = new(cx + 14, cy - 6);
          g.DrawLines(checkShadow, [p1, p2, p3]);
          g.DrawLines(checkPen, [p1, p2, p3]);
        }
      }
    }

    protected override void OnMouseDown(MouseEventArgs e) {
      base.OnMouseDown(e);
      if (e.Button != MouseButtons.Left || _colors == null || _selected == null) return;
      var count = _colors.Length / 3;
      var cols = _ColumnCount();
      var stride = _SwatchSize + _SwatchSpacing;
      var col = (e.X - 2) / stride;
      var row = (e.Y - 2) / stride;
      if (col < 0 || col >= cols) return;
      var idx = row * cols + col;
      if (idx < 0 || idx >= count) return;
      _selected[idx] = !_selected[idx];
      Invalidate();
      SwatchToggled?.Invoke(idx);
    }
  }
}
