using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FileFormat.Core;

namespace Crush.Viewer;

/// <summary>
/// Modal dialog that lets the user pick one of a format's <see cref="VideoMode"/> entries before saving.
/// Each radio button shows the mode's name + a one-line summary of dimensions, palette-size options, and any
/// pre-defined palettes available within that mode. The mode whose dimensions are closest to the current
/// source image is pre-selected (matches <see cref="Optimizer.Image.SaveAsPlanner.PickClosestMode"/>).
/// </summary>
internal sealed class VideoModeDialog : Form {

  /// <summary>The mode the user picked, or <c>null</c> if the dialog was cancelled.</summary>
  public VideoMode? PickedMode { get; private set; }

  private readonly RadioButton[] _radios;
  private readonly VideoMode[] _modes;

  public VideoModeDialog(VideoMode[] modes, int preselectIndex, int sourceWidth, int sourceHeight) {
    ArgumentNullException.ThrowIfNull(modes);
    if (modes.Length == 0) throw new ArgumentException("At least one mode is required.", nameof(modes));

    this._modes = modes;
    this._radios = new RadioButton[modes.Length];

    this.Text = "Choose video mode";
    this.ClientSize = new(460, Math.Max(160, 60 + modes.Length * 48));
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;
    this.StartPosition = FormStartPosition.CenterParent;

    var header = new Label {
      Text = $"Source: {sourceWidth} × {sourceHeight}. Pick one of {modes.Length} modes the target format supports:",
      Location = new(12, 12),
      Size = new(this.ClientSize.Width - 24, 32),
      ForeColor = SystemColors.GrayText,
    };
    this.Controls.Add(header);

    var y = 50;
    for (var i = 0; i < modes.Length; ++i) {
      var mode = modes[i];
      this._radios[i] = new RadioButton {
        Text = _FormatModeLabel(mode),
        Location = new(20, y),
        Size = new(this.ClientSize.Width - 32, 42),
        Checked = i == preselectIndex,
        Tag = i,
      };
      if (!string.IsNullOrEmpty(mode.Description)) {
        var tip = new ToolTip();
        tip.SetToolTip(this._radios[i], mode.Description);
      }
      this.Controls.Add(this._radios[i]);
      y += 46;
    }

    var okBtn = new Button {
      Text = "OK",
      DialogResult = DialogResult.OK,
      Location = new(this.ClientSize.Width - 174, this.ClientSize.Height - 40),
      Size = new(75, 28),
      Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
    };
    var cancelBtn = new Button {
      Text = "Cancel",
      DialogResult = DialogResult.Cancel,
      Location = new(this.ClientSize.Width - 93, this.ClientSize.Height - 40),
      Size = new(75, 28),
      Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
    };
    this.Controls.Add(okBtn);
    this.Controls.Add(cancelBtn);
    this.AcceptButton = okBtn;
    this.CancelButton = cancelBtn;

    okBtn.Click += (_, _) => {
      for (var i = 0; i < this._radios.Length; ++i)
        if (this._radios[i].Checked) {
          this.PickedMode = this._modes[i];
          return;
        }
    };
  }

  /// <summary>Builds a label like "Low resolution — 320×200, 16 colours" or
  /// "4-colour — 320×200, 4 colours (4 palettes available)".</summary>
  private static string _FormatModeLabel(VideoMode mode) {
    var dims = string.Join(" / ", mode.Dimensions.Select(_FormatDim));
    var sb = new System.Text.StringBuilder();
    sb.Append(mode.Name);
    sb.Append(" — ");
    sb.Append(dims);
    if (mode.AllowedPaletteRanges is { Length: > 0 } ranges) {
      sb.Append(", ");
      sb.Append(_FormatColours(ranges));
    }
    if (mode.AvailablePalettes is { Length: > 0 } palettes) {
      sb.Append(palettes.Length == 1 ? $" ({palettes[0].Name})" : $" ({palettes.Length} palettes)");
    }
    return sb.ToString();
  }

  private static string _FormatDim((IntegerRange Width, IntegerRange Height) d) {
    var w = _FormatAxis(d.Width);
    var h = _FormatAxis(d.Height);
    return $"{w}×{h}";
  }

  private static string _FormatAxis(IntegerRange r) {
    if (r.Min == int.MaxValue || r.Max == int.MaxValue) return "any";
    if (r.IsFixed) return r.Min.ToString();
    if (r.Step > 1) return $"{r.Min}..{r.Max}/{r.Step}";
    return $"{r.Min}..{r.Max}";
  }

  private static string _FormatColours(IntegerRange[] ranges) {
    if (ranges.Length == 1) {
      var r = ranges[0];
      return r.IsFixed ? $"{r.Min} colours" : $"{r.Min}..{r.Max} colours";
    }
    return $"{ranges.Length} colour-size options";
  }
}
