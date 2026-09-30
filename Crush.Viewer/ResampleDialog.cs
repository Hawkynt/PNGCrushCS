using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Core;
using Hawkynt.ImageProcessing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Optimizer.Image;

namespace Crush.Viewer;

/// <summary>How a resample decides the size it ends up at.</summary>
internal enum ResampleTarget {

  /// <summary>Exactly the width and height asked for.</summary>
  ExactSize,

  /// <summary>The largest size inside the width and height asked for, keeping the aspect ratio.</summary>
  FitInside,

  /// <summary>
  /// The same physical size at a new density, which only a picture that states its density can do.
  /// </summary>
  PrintDensity,
}

/// <summary>Asks for a resampling kernel and the size to resample to, and previews the kernel.</summary>
/// <remarks>
/// <para>
/// One command with a kernel chooser rather than a button per kernel. Six kernels as six buttons is
/// six copies of the same size dialog behind them, and the ribbon cannot say on a button what the
/// dialog then has to ask anyway; with the chooser inside, the kernel sits next to the preview that
/// shows what it does, which is the only place the difference between Mitchell and Lanczos can be
/// seen rather than described.
/// </para>
/// <para>
/// The preview is the middle of the picture resampled by the same factor, so it shows the kernel's
/// character — ringing, softness, blockiness — at the scale the picture will get it, without
/// resampling the whole picture on every change.
/// </para>
/// </remarks>
internal sealed class ResampleDialog : DialogForm {

  private const int _LARGEST = 16384;
  private const int _SETTLE_MS = 150;

  private readonly IPlatformBackend _backend;
  private readonly RawImage _source;
  private readonly double? _densityX;
  private readonly double? _densityY;
  private readonly ComboBox _kernel;
  private readonly ComboBox _target;
  private readonly NumericUpDown _width;
  private readonly NumericUpDown _height;
  private readonly CheckBox _lockAspect;
  private readonly NumericUpDown _density;
  private readonly PictureBox _preview;
  private readonly Label _status;
  private readonly Timer _settle = new() { Interval = _SETTLE_MS };
  private readonly double _ratio;
  private IImage? _shown;
  private int _generation;
  private bool _mirroring;

  internal ResampleDialog(IPlatformBackend backend, RawImage source) : base("Resample picture") {
    this._backend = backend;
    this._source = source;
    this._ratio = source.Width / (double)Math.Max(1, source.Height);
    this._densityX = source.Metadata?.DpiX is > 0 ? source.Metadata.DpiX : null;
    this._densityY = source.Metadata?.DpiY is > 0 ? source.Metadata.DpiY : null;

    var density = this._densityX is { } dx && this._densityY is { } dy
      ? $" It states {dx:0.##} x {dy:0.##} dpi."
      : " It does not state a density, so it can only be resampled to a pixel size.";
    this.AddParagraph($"The picture is {source.Width} x {source.Height} pixels.{density}");

    this._kernel = this.AddRow("Kernel", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
    foreach (var name in PixelCommands.KernelNames)
      this._kernel.Items.Add(name);
    this._kernel.SelectedIndex = Array.IndexOf(PixelCommands.Kernels, ResampleKernel.Lanczos);

    this._target = this.AddRow("Size", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
    this._target.Items.Add("Exactly this width and height");
    this._target.Items.Add("Fit inside this width and height");
    if (this._densityX != null && this._densityY != null)
      this._target.Items.Add("Keep the print size, change the density");
    this._target.SelectedIndex = 0;

    this._width = this.AddRow("Width", _Spinner(source.Width));
    this._height = this.AddRow("Height", _Spinner(source.Height));
    this._lockAspect = this.AddWide(new CheckBox { Text = "Keep the aspect ratio", Checked = true });
    this._density = this.AddRow("Density (dpi)", new NumericUpDown {
      Minimum = 1,
      Maximum = 9600,
      Increment = 1,
      DecimalPlaces = 0,
      Value = (decimal)Math.Clamp(Math.Round(this._densityX ?? 72), 1, 9600),
    });

    this._preview = this.AddWide(new PictureBox { SizeMode = PictureBoxSizeMode.Zoom }, 220);
    this._status = this.AddNote("", 22);
    this.AddButtons("Resample");

    this._width.ValueChanged += (_, _) => {
      this._Mirror(this._width, this._height, 1 / this._ratio);
      this._Changed();
    };
    this._height.ValueChanged += (_, _) => {
      this._Mirror(this._height, this._width, this._ratio);
      this._Changed();
    };
    this._kernel.SelectedIndexChanged += (_, _) => this._Changed();
    this._target.SelectedIndexChanged += (_, _) => {
      this._UpdateEnabled();
      this._Changed();
    };
    this._density.ValueChanged += (_, _) => this._Changed();
    this._settle.Tick += (_, _) => {
      this._settle.Stop();
      this._Render();
    };

    this._UpdateEnabled();
    this.Load += (_, _) => this._Render();
    this.FormClosed += (_, _) => {
      this._settle.Stop();
      Interlocked.Increment(ref this._generation);
      this._preview.Image = null;
      this._shown?.Dispose();
      this._shown = null;
    };
  }

  /// <summary>The kernel the user picked.</summary>
  internal ResampleKernel Kernel => PixelCommands.Kernels[Math.Max(0, this._kernel.SelectedIndex)];

  /// <summary>How the size is decided.</summary>
  internal ResampleTarget Target => (ResampleTarget)Math.Max(0, this._target.SelectedIndex);

  /// <summary>The resample as the dialog stands, ready to run on the picture.</summary>
  internal Func<RawImage, RawImage> Operation {
    get {
      var kernel = this.Kernel;
      var width = (int)this._width.Value;
      var height = (int)this._height.Value;
      var density = (double)this._density.Value;
      var densityY = this._densityX is { } x && this._densityY is { } y ? density * y / x : density;
      return this.Target switch {
        ResampleTarget.FitInside => image => ImageScaler.Fit(image, width, height, kernel),
        ResampleTarget.PrintDensity => image => ImageScaler.Resample(image, density, densityY, kernel),
        _ => image => ImageScaler.Resize(image, width, height, kernel),
      };
    }
  }

  /// <summary>The size the picture will come out at, which the preview's factor is taken from.</summary>
  private (int Width, int Height) _Outcome() {
    var width = (double)this._width.Value;
    var height = (double)this._height.Value;
    switch (this.Target) {
      case ResampleTarget.FitInside: {
        var factor = Math.Min(width / this._source.Width, height / this._source.Height);
        return ((int)Math.Round(this._source.Width * factor), (int)Math.Round(this._source.Height * factor));
      }
      case ResampleTarget.PrintDensity when this._densityX is { } x: {
        var factor = (double)this._density.Value / x;
        return ((int)Math.Round(this._source.Width * factor), (int)Math.Round(this._source.Height * factor));
      }
      default:
        return ((int)width, (int)height);
    }
  }

  protected override bool Validate(out string error) {
    var (width, height) = this._Outcome();
    if (width is < 1 or > _LARGEST || height is < 1 or > _LARGEST) {
      error = $"That would make the picture {width} x {height} pixels; both sides have to be from 1 to {_LARGEST}.";
      return false;
    }

    error = "";
    return true;
  }

  private void _UpdateEnabled() {
    var density = this.Target == ResampleTarget.PrintDensity;
    this._width.Enabled = !density;
    this._height.Enabled = !density;
    this._lockAspect.Enabled = this.Target == ResampleTarget.ExactSize;
    this._density.Enabled = density;
  }

  private void _Changed() {
    this._settle.Stop();
    this._settle.Start();
  }

  /// <summary>Resamples the middle of the picture by the factor the whole will get.</summary>
  private void _Render() {
    var generation = Interlocked.Increment(ref this._generation);
    var (width, height) = this._Outcome();
    var kernel = this.Kernel;
    var source = this._source;
    this._status.Text = "Working…";

    Task.Run(() => {
      var clock = Stopwatch.StartNew();
      try {
        var factorX = Math.Max(1e-3, width / (double)source.Width);
        var factorY = Math.Max(1e-3, height / (double)source.Height);
        var cropWidth = Math.Clamp((int)Math.Ceiling(PixelCommandDialog.PREVIEW_WIDTH / factorX), 1, source.Width);
        var cropHeight = Math.Clamp((int)Math.Ceiling(220 / factorY), 1, source.Height);
        var crop = ImageTransformer.Crop(source, new PixelRect(
          (source.Width - cropWidth) / 2, (source.Height - cropHeight) / 2, cropWidth, cropHeight));
        var result = ImageScaler.Resize(
          crop,
          Math.Max(1, (int)Math.Round(cropWidth * factorX)),
          Math.Max(1, (int)Math.Round(cropHeight * factorY)),
          kernel);
        var pixels = ImageBridge.ToArgb(result);
        var elapsed = clock.Elapsed;
        this.BeginInvoke(() => this._Show(generation, result.Width, result.Height, pixels, width, height, elapsed));
      } catch (Exception ex) {
        this.BeginInvoke(() => {
          if (generation == Volatile.Read(ref this._generation))
            this._status.Text = $"The preview failed: {ex.Message}";
        });
      }
    });
  }

  private void _Show(int generation, int width, int height, int[] pixels, int targetWidth, int targetHeight, TimeSpan elapsed) {
    if (generation != Volatile.Read(ref this._generation))
      return;

    var previous = this._shown;
    this._shown = this._backend.CreateImage(width, height, pixels);
    this._preview.Image = this._shown;
    previous?.Dispose();
    this._status.Text = $"The middle, as it comes out at {targetWidth} x {targetHeight}. {elapsed.TotalMilliseconds:0} ms.";
  }

  private static NumericUpDown _Spinner(int value) => new() {
    Minimum = 1,
    Maximum = _LARGEST,
    Increment = 1,
    Value = Math.Clamp(value, 1, _LARGEST),
  };

  private void _Mirror(NumericUpDown from, NumericUpDown to, double factor) {
    if (this._mirroring || !this._lockAspect.Checked || this.Target != ResampleTarget.ExactSize)
      return;

    this._mirroring = true;
    to.Value = Math.Clamp(Math.Round(from.Value * (decimal)factor), to.Minimum, to.Maximum);
    this._mirroring = false;
  }
}
