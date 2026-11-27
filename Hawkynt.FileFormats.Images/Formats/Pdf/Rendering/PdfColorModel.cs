using System;
using System.Collections.Generic;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>A colour space (PDF 32000-1 §8.6), able to turn its components into display RGB.</summary>
internal abstract class PdfColorModel {

  /// <summary>How many components a colour in this space has.</summary>
  public abstract int Components { get; }

  /// <summary>The space's name, for messages.</summary>
  public abstract string Name { get; }

  /// <summary>The colour a space starts with when selected (§8.6.8): black, or the lowest index.</summary>
  public virtual double[] InitialColor() {
    var c = new double[this.Components];
    return c;
  }

  /// <summary>The default decode range for one component of an image at a given depth (§8.9.5.2).</summary>
  public virtual (double Min, double Max) DefaultDecode(int component, int bitsPerComponent) => (0, 1);

  /// <summary>Converts a colour to RGB, each in 0..1.</summary>
  public abstract void ToRgb(ReadOnlySpan<double> components, Span<double> rgb);

  /// <summary>Whether this is the special Pattern space.</summary>
  public virtual bool IsPattern => false;

  /// <summary>Whether painting in this colour paints nothing — a Separation of /None.</summary>
  public virtual bool PaintsNothing => false;

  public static readonly PdfColorModel DeviceGray = new GraySpace();
  public static readonly PdfColorModel DeviceRgb = new RgbSpace();
  public static readonly PdfColorModel DeviceCmyk = new CmykSpace();

  /// <summary>Reads a colour space from its name or array, looking names up in the resources.</summary>
  public static PdfColorModel Parse(PdfDocument document, object? value, PdfDict? resources, int depth = 0) {
    if (depth > 8)
      throw new NotSupportedException("PDF colour spaces nested more than eight deep are not supported.");

    value = document.Resolve(value);
    if (value is string name) {
      switch (name) {
        case "DeviceGray" or "G":
          return _Default(document, resources, "DefaultGray", DeviceGray, depth);
        case "DeviceRGB" or "RGB":
          return _Default(document, resources, "DefaultRGB", DeviceRgb, depth);
        case "DeviceCMYK" or "CMYK":
          return _Default(document, resources, "DefaultCMYK", DeviceCmyk, depth);
        case "Pattern":
          return new PatternSpace(null);
        case "Indexed" or "I":
          throw new NotSupportedException("An /Indexed colour space needs its array form.");
      }

      var named = document.Dict(document.Get(resources, "ColorSpace"))?.Raw(name);
      if (named != null)
        return Parse(document, named, resources, depth + 1);

      throw new NotSupportedException($"The PDF colour space /{name} is not defined.");
    }

    if (value is not List<object?> { Count: > 0 } array || document.Resolve(array[0]) is not string family)
      throw new NotSupportedException("A PDF colour space is neither a name nor an array.");

    switch (family) {
      case "DeviceGray" or "G" or "DeviceRGB" or "RGB" or "DeviceCMYK" or "CMYK" when array.Count == 1:
        return Parse(document, family, resources, depth + 1);
      case "CalGray":
        return new CieSpace(1, document.Dict(array.Count > 1 ? array[1] : null), document, CieKind.CalGray);
      case "CalRGB":
        return new CieSpace(3, document.Dict(array.Count > 1 ? array[1] : null), document, CieKind.CalRgb);
      case "Lab":
        return new CieSpace(3, document.Dict(array.Count > 1 ? array[1] : null), document, CieKind.Lab);
      case "ICCBased": {
        var stream = document.Stream(array.Count > 1 ? array[1] : null);
        var dict = stream?.Dictionary;
        var n = document.Integer(dict, "N", 0);
        PdfColorModel? alternate = null;
        if (dict?.Raw("Alternate") is { } alt) {
          try {
            alternate = Parse(document, alt, resources, depth + 1);
          } catch (NotSupportedException) {
            alternate = null;
          }
        }

        if (alternate == null || (n > 0 && alternate.Components != n))
          alternate = n switch {
            1 => DeviceGray,
            3 => DeviceRgb,
            4 => DeviceCmyk,
            _ => throw new NotSupportedException($"An ICCBased colour space with {n} components and no usable /Alternate is not supported."),
          };

        var profiled = stream == null ? null : PdfIccProfile.TryCreate(document, stream, alternate.Components);
        return profiled ?? (PdfColorModel)new IccSpace(alternate, document.Numbers(dict?.Raw("Range")));
      }
      case "Indexed" or "I": {
        if (array.Count < 4)
          throw new NotSupportedException("An /Indexed colour space has fewer than four entries.");

        var baseSpace = Parse(document, array[1], resources, depth + 1);
        var hival = Math.Clamp(PdfValues.Integer(document.Resolve(array[2])), 0, 255);
        var lookupValue = document.Resolve(array[3]);
        var lookup = lookupValue switch {
          byte[] bytes => bytes,
          PdfStreamObject s => document.Decode(s),
          _ => throw new NotSupportedException("An /Indexed colour space's lookup table is neither a string nor a stream."),
        };
        return new IndexedSpace(baseSpace, hival, lookup);
      }
      case "Separation": {
        if (array.Count < 4)
          throw new NotSupportedException("A /Separation colour space has fewer than four entries.");

        var colorant = document.Resolve(array[1]) as string;
        var alternate = Parse(document, array[2], resources, depth + 1);
        if (colorant == "None")
          return new TintSpace(1, alternate, null, "Separation /None", true);
        if (colorant == "All")
          return new TintSpace(1, alternate, null, "Separation /All", false);

        return new TintSpace(1, alternate, PdfFunction.Parse(document, array[3]), $"Separation /{colorant}", false);
      }
      case "DeviceN": {
        if (array.Count < 4)
          throw new NotSupportedException("A /DeviceN colour space has fewer than four entries.");

        var names = document.Array(array[1]) ?? [];
        var alternate = Parse(document, array[2], resources, depth + 1);
        var allNone = names.Count > 0;
        foreach (var n in names)
          allNone &= document.Resolve(n) as string == "None";
        return new TintSpace(names.Count, alternate, allNone ? null : PdfFunction.Parse(document, array[3]), "DeviceN", allNone);
      }
      case "Pattern":
        return new PatternSpace(array.Count > 1 ? Parse(document, array[1], resources, depth + 1) : null);
      default:
        throw new NotSupportedException($"The PDF colour space /{family} is not supported.");
    }
  }

  private static PdfColorModel _Default(PdfDocument document, PdfDict? resources, string key, PdfColorModel device, int depth) {
    // §8.6.5.6: a page may redirect a device space to a CIE-based one through its resources.
    var replacement = document.Dict(document.Get(resources, "ColorSpace"))?.Raw(key);
    if (replacement == null)
      return device;

    try {
      var space = Parse(document, replacement, null, depth + 1);
      return space.Components == device.Components ? space : device;
    } catch (NotSupportedException) {
      return device;
    }
  }

  private sealed class GraySpace : PdfColorModel {
    public override int Components => 1;
    public override string Name => "DeviceGray";
    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) => rgb[0] = rgb[1] = rgb[2] = Math.Clamp(c[0], 0, 1);
  }

  private sealed class RgbSpace : PdfColorModel {
    public override int Components => 3;
    public override string Name => "DeviceRGB";

    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) {
      rgb[0] = Math.Clamp(c[0], 0, 1);
      rgb[1] = Math.Clamp(c[1], 0, 1);
      rgb[2] = Math.Clamp(c[2], 0, 1);
    }
  }

  /// <summary>
  /// DeviceCMYK by multilinear interpolation between the sixteen corner colours of the CMYK cube.
  /// </summary>
  /// <remarks>
  /// The corner colours are the table xpdf published and poppler uses; measured from pdftoppm's own
  /// renders, its conversion is exactly this interpolation (every one of 48 random colours agreed to
  /// the unit), so matching it is matching the oracle. The naive 1 − (c + k) is far from what a
  /// viewer shows for a rich black or a process blue.
  /// </remarks>
  private sealed class CmykSpace : PdfColorModel {
    private static readonly double[] _Corners = [
      // c m y k  ->  r g b, in the order of the index bits c=8 m=4 y=2 k=1
      1, 1, 1,                0.1373, 0.1216, 0.1255,
      1, 0.9490, 0,           0.1098, 0.1020, 0,
      0.9255, 0, 0.5490,      0.1412, 0, 0,
      0.9294, 0.1098, 0.1412, 0.1333, 0, 0,
      0, 0.6784, 0.9373,      0, 0.0588, 0.1412,
      0, 0.6510, 0.3137,      0, 0.0745, 0,
      0.1804, 0.1922, 0.5725, 0, 0, 0.0078,
      0.2118, 0.2119, 0.2235, 0, 0, 0,
    ];

    public override int Components => 4;
    public override string Name => "DeviceCMYK";

    public override double[] InitialColor() => [0, 0, 0, 1];

    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) => Convert(Math.Clamp(c[0], 0, 1), Math.Clamp(c[1], 0, 1), Math.Clamp(c[2], 0, 1), Math.Clamp(c[3], 0, 1), rgb);

    public static void Convert(double c, double m, double y, double k, Span<double> rgb) {
      double r = 0, g = 0, b = 0;
      for (var corner = 0; corner < 16; ++corner) {
        var w = ((corner & 8) != 0 ? c : 1 - c) * ((corner & 4) != 0 ? m : 1 - m) * ((corner & 2) != 0 ? y : 1 - y) * ((corner & 1) != 0 ? k : 1 - k);
        if (w == 0)
          continue;
        r += w * _Corners[corner * 3];
        g += w * _Corners[corner * 3 + 1];
        b += w * _Corners[corner * 3 + 2];
      }

      rgb[0] = r;
      rgb[1] = g;
      rgb[2] = b;
    }
  }

  /// <summary>Converts CMYK the way DeviceCMYK does, for callers outside a colour space.</summary>
  public static void CmykToRgb(double c, double m, double y, double k, Span<double> rgb) => CmykSpace.Convert(c, m, y, k, rgb);

  private enum CieKind { CalGray, CalRgb, Lab }

  /// <summary>
  /// CalGray, CalRGB and Lab: to XYZ relative to the space's white point, Bradford-adapted to D65,
  /// then to sRGB.
  /// </summary>
  /// <remarks>
  /// §8.6.5 defines the transforms into XYZ; what happens after is the viewer's choice, and this is
  /// the one poppler makes — checked against its output for CalGray at two gammas, CalRGB with and
  /// without a matrix, and Lab under D65 and D50 white points, all to the unit.
  /// </remarks>
  private sealed class CieSpace : PdfColorModel {
    private readonly int _n;
    private readonly CieKind _kind;
    private readonly double[] _white = [0.9505, 1, 1.089];
    private readonly double[] _gamma = [1, 1, 1];
    private readonly double[] _matrix = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    private readonly double[] _range = [-100, 100, -100, 100];
    private readonly double[] _adapt;

    public CieSpace(int n, PdfDict? dict, PdfDocument document, CieKind kind) {
      this._n = n;
      this._kind = kind;
      if (document.Numbers(dict?.Raw("WhitePoint")) is { Length: 3 } white && white[1] > 0)
        this._white = white;

      if (kind == CieKind.CalGray)
        this._gamma[0] = document.Number(dict, "Gamma", 1);
      else if (kind == CieKind.CalRgb) {
        if (document.Numbers(dict?.Raw("Gamma")) is { Length: 3 } g)
          this._gamma = g;
        if (document.Numbers(dict?.Raw("Matrix")) is { Length: 9 } m)
          this._matrix = m;
      } else if (document.Numbers(dict?.Raw("Range")) is { Length: 4 } r)
        this._range = r;

      this._adapt = Bradford(this._white);
    }

    public override int Components => this._n;
    public override string Name => this._kind.ToString();

    public override double[] InitialColor() {
      if (this._kind != CieKind.Lab)
        return new double[this._n];

      // Lab's initial colour is L = 0 with a and b at zero or clamped into their ranges.
      return [0, Math.Clamp(0, this._range[0], this._range[1]), Math.Clamp(0, this._range[2], this._range[3])];
    }

    public override (double Min, double Max) DefaultDecode(int component, int bitsPerComponent)
      => this._kind == CieKind.Lab ? component == 0 ? (0, 100) : (this._range[2 * component - 2], this._range[2 * component - 1]) : (0, 1);

    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) {
      double x, y, z;
      switch (this._kind) {
        case CieKind.CalGray: {
          var a = Math.Pow(Math.Clamp(c[0], 0, 1), this._gamma[0]);
          x = this._white[0] * a;
          y = this._white[1] * a;
          z = this._white[2] * a;
          break;
        }
        case CieKind.CalRgb: {
          var a = Math.Pow(Math.Clamp(c[0], 0, 1), this._gamma[0]);
          var b = Math.Pow(Math.Clamp(c[1], 0, 1), this._gamma[1]);
          var cc = Math.Pow(Math.Clamp(c[2], 0, 1), this._gamma[2]);
          var m = this._matrix;
          x = m[0] * a + m[3] * b + m[6] * cc;
          y = m[1] * a + m[4] * b + m[7] * cc;
          z = m[2] * a + m[5] * b + m[8] * cc;
          break;
        }
        default: {
          var l = Math.Clamp(c[0], 0, 100);
          var la = Math.Clamp(c[1], this._range[0], this._range[1]);
          var lb = Math.Clamp(c[2], this._range[2], this._range[3]);
          var fy = (l + 16) / 116;
          var fx = fy + la / 500;
          var fz = fy - lb / 200;
          x = this._white[0] * _LabInverse(fx);
          y = this._white[1] * _LabInverse(fy);
          z = this._white[2] * _LabInverse(fz);
          break;
        }
      }

      XyzToRgb(this._adapt, x, y, z, rgb);
    }

    private static double _LabInverse(double t) => t > 6.0 / 29 ? t * t * t : 3 * (6.0 / 29) * (6.0 / 29) * (t - 4.0 / 29);
  }

  /// <summary>The Bradford matrix adapting a white point to D65, row-major.</summary>
  public static double[] Bradford(double[] white) {
    double[] b = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];
    double[] bi = [0.9869929, -0.1470543, 0.1599627, 0.4323053, 0.5183603, 0.0492912, -0.0085287, 0.0400428, 0.9684867];
    double[] d65 = [0.9505, 1, 1.089];
    Span<double> src = stackalloc double[3];
    Span<double> dst = stackalloc double[3];
    for (var i = 0; i < 3; ++i) {
      src[i] = b[i * 3] * white[0] + b[i * 3 + 1] * white[1] + b[i * 3 + 2] * white[2];
      dst[i] = b[i * 3] * d65[0] + b[i * 3 + 1] * d65[1] + b[i * 3 + 2] * d65[2];
    }

    var m = new double[9];
    for (var i = 0; i < 3; ++i)
      for (var j = 0; j < 3; ++j) {
        var sum = 0.0;
        for (var k = 0; k < 3; ++k)
          sum += bi[i * 3 + k] * (dst[k] / src[k]) * b[k * 3 + j];
        m[i * 3 + j] = sum;
      }

    return m;
  }

  /// <summary>Adapted XYZ (D65) to sRGB with the sRGB transfer curve.</summary>
  public static void XyzToRgb(double[] adapt, double x, double y, double z, Span<double> rgb) {
    var ax = adapt[0] * x + adapt[1] * y + adapt[2] * z;
    var ay = adapt[3] * x + adapt[4] * y + adapt[5] * z;
    var az = adapt[6] * x + adapt[7] * y + adapt[8] * z;
    rgb[0] = SrgbEncode(3.2406 * ax - 1.5372 * ay - 0.4986 * az);
    rgb[1] = SrgbEncode(-0.9689 * ax + 1.8758 * ay + 0.0415 * az);
    rgb[2] = SrgbEncode(0.0557 * ax - 0.2040 * ay + 1.0570 * az);
  }

  /// <summary>The sRGB transfer function, linear light to encoded.</summary>
  public static double SrgbEncode(double v) {
    v = Math.Clamp(v, 0, 1);
    return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
  }

  /// <summary>An ICC-based space drawn through its alternate, with its declared component ranges.</summary>
  private sealed class IccSpace(PdfColorModel alternate, double[]? range) : PdfColorModel {
    public override int Components => alternate.Components;
    public override string Name => "ICCBased";
    public override double[] InitialColor() => alternate.InitialColor();
    public override (double Min, double Max) DefaultDecode(int component, int bitsPerComponent)
      => range != null && range.Length >= 2 * component + 2 ? (range[2 * component], range[2 * component + 1]) : alternate.DefaultDecode(component, bitsPerComponent);
    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) => alternate.ToRgb(c, rgb);
  }

  private sealed class IndexedSpace(PdfColorModel baseSpace, int hival, byte[] lookup) : PdfColorModel {
    public override int Components => 1;
    public override string Name => "Indexed";
    public override (double Min, double Max) DefaultDecode(int component, int bitsPerComponent) => (0, (1 << bitsPerComponent) - 1);

    public PdfColorModel Base => baseSpace;

    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) {
      var index = Math.Clamp((int)Math.Round(c[0]), 0, hival);
      var n = baseSpace.Components;
      Span<double> values = stackalloc double[n];
      for (var i = 0; i < n; ++i) {
        var at = index * n + i;
        var raw = at < lookup.Length ? lookup[at] / 255.0 : 0;
        var (lo, hi) = baseSpace.DefaultDecode(i, 8);
        values[i] = lo + raw * (hi - lo);
      }

      baseSpace.ToRgb(values, rgb);
    }
  }

  /// <summary>Separation and DeviceN: tints through the tint transform into the alternate space.</summary>
  private sealed class TintSpace(int n, PdfColorModel alternate, PdfFunction? transform, string name, bool none) : PdfColorModel {
    private readonly Dictionary<long, (double, double, double)> _cache = [];

    public override int Components => n;
    public override string Name => name;
    public override bool PaintsNothing => none;

    public override double[] InitialColor() {
      var c = new double[n];
      Array.Fill(c, 1.0);
      return c;
    }

    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) {
      if (transform == null) {
        // /All: every separation at the tint, which on a composite device is grey (§8.6.6.4).
        var v = 1 - Math.Clamp(c[0], 0, 1);
        rgb[0] = rgb[1] = rgb[2] = v;
        return;
      }

      // Tints are cached quantised to 1/4096, which is finer than any output can show; images run
      // the same few tints through the transform millions of times.
      long key = 0;
      var cacheable = n <= 5;
      if (cacheable)
        for (var i = 0; i < n; ++i)
          key = key * 4097 + (long)Math.Round(Math.Clamp(c[i], 0, 1) * 4096);

      if (cacheable && this._cache.TryGetValue(key, out var hit)) {
        (rgb[0], rgb[1], rgb[2]) = hit;
        return;
      }

      Span<double> output = stackalloc double[Math.Max(alternate.Components, transform.Outputs)];
      output.Clear();
      transform.Evaluate(c, output);
      alternate.ToRgb(output, rgb);
      if (cacheable && this._cache.Count < 65536)
        this._cache[key] = (rgb[0], rgb[1], rgb[2]);
    }
  }

  /// <summary>The Pattern space: colours are patterns, and an uncoloured one carries components in an underlying space.</summary>
  internal sealed class PatternSpace(PdfColorModel? underlying) : PdfColorModel {
    public PdfColorModel? Underlying => underlying;
    public override int Components => underlying?.Components ?? 0;
    public override string Name => "Pattern";
    public override bool IsPattern => true;
    public override void ToRgb(ReadOnlySpan<double> c, Span<double> rgb) {
      if (underlying != null)
        underlying.ToRgb(c, rgb);
      else
        rgb[0] = rgb[1] = rgb[2] = 0;
    }
  }
}
