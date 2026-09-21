using System;
using Hawkynt.NativeForms;
using Optimizer.Image;

namespace Crush.Viewer;

/// <summary>Asks for the size a picture should be scaled to and how.</summary>
internal sealed class ResizeOptionsDialog : DialogForm {

  private readonly NumericUpDown _width;
  private readonly NumericUpDown _height;
  private readonly CheckBox _lockAspect;
  private readonly ComboBox _mode;
  private readonly ComboBox _interpolation;
  private readonly double _ratio;
  private bool _mirroring;

  internal ResizeOptionsDialog(int sourceWidth, int sourceHeight, InterpolationHint hint) : base("Resize picture") {
    this._ratio = sourceWidth / (double)Math.Max(1, sourceHeight);
    this.AddNote($"The picture is {sourceWidth} x {sourceHeight} pixels.", 24);

    this._width = this.AddRow("Width", _Spinner(sourceWidth));
    this._height = this.AddRow("Height", _Spinner(sourceHeight));
    this._lockAspect = this.AddWide(new CheckBox { Text = "Keep the aspect ratio", Checked = true });

    this._width.ValueChanged += (_, _) => this._Mirror(this._width, this._height, 1 / this._ratio);
    this._height.ValueChanged += (_, _) => this._Mirror(this._height, this._width, this._ratio);

    this._mode = this.AddRow("Mode", _Choice([ResizeMode.Stretch, ResizeMode.Fit, ResizeMode.Fill], 0));
    this._interpolation = this.AddRow("Interpolation", _Choice(
      [InterpolationHint.NearestNeighbor, InterpolationHint.Bilinear, InterpolationHint.Bicubic],
      hint switch { InterpolationHint.NearestNeighbor => 0, InterpolationHint.Bilinear => 1, _ => 2 }));

    this.AddButtons("Resize");
  }

  /// <summary>The width the picture should end up.</summary>
  internal int TargetWidth => (int)this._width.Value;

  /// <summary>The height the picture should end up.</summary>
  internal int TargetHeight => (int)this._height.Value;

  /// <summary>Whether to stretch, letterbox or crop to reach that size.</summary>
  internal ResizeMode Mode => (ResizeMode)this._mode.SelectedItem!;

  /// <summary>Which filter the resampler should use.</summary>
  internal InterpolationHint Interpolation => (InterpolationHint)this._interpolation.SelectedItem!;

  private static NumericUpDown _Spinner(int value) => new() {
    Minimum = 1,
    Maximum = 16384,
    Increment = 1,
    Value = Math.Clamp(value, 1, 16384),
  };

  private static ComboBox _Choice<T>(T[] values, int selected) where T : struct, Enum {
    var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplaySelector = o => o?.ToString() ?? "" };
    foreach (var value in values)
      box.Items.Add(value);
    box.SelectedIndex = Math.Clamp(selected, 0, values.Length - 1);
    return box;
  }

  private void _Mirror(NumericUpDown from, NumericUpDown to, double factor) {
    if (this._mirroring || !this._lockAspect.Checked)
      return;

    this._mirroring = true;
    to.Value = Math.Clamp(Math.Round(from.Value * (decimal)factor), to.Minimum, to.Maximum);
    this._mirroring = false;
  }
}

/// <summary>Asks for a single whole number inside a range.</summary>
internal sealed class NumberDialog : DialogForm {

  private readonly NumericUpDown _value;

  internal NumberDialog(string title, string note, string label, int minimum, int maximum, int value, string acceptText)
    : base(title) {
    this.AddNote(note, 36);
    this._value = this.AddRow(label, new NumericUpDown {
      Minimum = minimum,
      Maximum = maximum,
      Increment = 1,
      Value = Math.Clamp(value, minimum, maximum),
    });

    this.AddButtons(acceptText);
  }

  /// <summary>The number the user settled on.</summary>
  internal int Value => (int)this._value.Value;
}
