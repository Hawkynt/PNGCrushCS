using System;
using System.Collections.Generic;

namespace FileFormat.Pdf.Rendering.Icc;

/// <summary>Constants and the fixed-point helpers of Little CMS (lcms2.h, lcms2_internal.h).</summary>
internal static class IccMath {
  public const int MaxChannels = 15;
  public const double D50X = 0.9642, D50Y = 1.0, D50Z = 0.8249;

  /// <summary>The largest XYZ the 1.15 fixed encoding holds; PCS XYZ travels as value / this.</summary>
  public const double MaxEncodeableXyz = 1.0 + 32767.0 / 32768.0;
  public const double InpAdj = 1.0 / MaxEncodeableXyz;
  public const double OutpAdj = MaxEncodeableXyz;
  public const double DeterminantTolerance = 0.0001;

  /// <summary>_cmsQuickSaturateWord: round half up into 0..65535.</summary>
  public static ushort SaturateWord(double d) {
    d += 0.5;
    if (d <= 0)
      return 0;
    if (d >= 65535.0)
      return 0xFFFF;
    return (ushort)Math.Floor(Math.Round(d * 65536.0) / 65536.0);
  }

  /// <summary>_cmsToFixedDomain: 0..0xFFFF scaled to 0..1.0 in 16.16.</summary>
  public static int ToFixedDomain(int a) => a + (a + 0x7FFF) / 0xFFFF;

  /// <summary>LinearInterp of cmsintrp.c.</summary>
  public static ushort LinearInterp(int a, int l, int h) => (ushort)((((long)(h - l) * a + 0x8000) >> 16) + l);

  public static bool IsIdentity(double[] m) {
    for (var i = 0; i < 9; ++i)
      if (Math.Abs(m[i] - (i % 4 == 0 ? 1 : 0)) > 1.0 / 65535.0)
        return false;
    return true;
  }

  /// <summary>cmsLab2XYZ against D50.</summary>
  public static (double X, double Y, double Z) LabToXyz(double l, double a, double b) {
    var fy = (l + 16.0) / 116.0;
    var fx = fy + 0.002 * a;
    var fz = fy - 0.005 * b;
    return (_FInverse(fx) * D50X, _FInverse(fy) * D50Y, _FInverse(fz) * D50Z);
  }

  /// <summary>cmsXYZ2Lab against D50.</summary>
  public static (double L, double A, double B) XyzToLab(double x, double y, double z) {
    var fx = _F(x / D50X);
    var fy = _F(y / D50Y);
    var fz = _F(z / D50Z);
    return (116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
  }

  private static double _F(double t) {
    const double limit = 24.0 / 116.0 * (24.0 / 116.0) * (24.0 / 116.0);
    return t <= limit ? 841.0 / 108.0 * t + 16.0 / 116.0 : Math.Pow(t, 1.0 / 3.0);
  }

  private static double _FInverse(double t) {
    const double limit = 24.0 / 116.0;
    return t <= limit ? 108.0 / 841.0 * (t - 16.0 / 116.0) : t * t * t;
  }

  public static double[] Multiply(double[] a, double[] b) {
    var r = new double[9];
    for (var i = 0; i < 3; ++i)
      for (var j = 0; j < 3; ++j)
        r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
    return r;
  }

  public static double[]? Inverse(double[] a) {
    var c0 = a[4] * a[8] - a[5] * a[7];
    var c1 = -a[3] * a[8] + a[5] * a[6];
    var c2 = a[3] * a[7] - a[4] * a[6];
    var det = a[0] * c0 + a[1] * c1 + a[2] * c2;
    if (Math.Abs(det) < 1e-9)
      return null;
    return [
      c0 / det, (a[2] * a[7] - a[1] * a[8]) / det, (a[1] * a[5] - a[2] * a[4]) / det,
      c1 / det, (a[0] * a[8] - a[2] * a[6]) / det, (a[2] * a[3] - a[0] * a[5]) / det,
      c2 / det, (a[1] * a[6] - a[0] * a[7]) / det, (a[0] * a[4] - a[1] * a[3]) / det,
    ];
  }

  public static (double, double, double) Apply(double[] m, double x, double y, double z)
    => (m[0] * x + m[1] * y + m[2] * z, m[3] * x + m[4] * y + m[5] * z, m[6] * x + m[7] * y + m[8] * z);

  /// <summary>_cmsAdaptationMatrix with the Bradford cone matrix (ComputeChromaticAdaptation).</summary>
  public static double[] Bradford((double X, double Y, double Z) from, (double X, double Y, double Z) to) {
    double[] cone = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];
    var inverse = Inverse(cone)!;
    var s = Apply(cone, from.X, from.Y, from.Z);
    var d = Apply(cone, to.X, to.Y, to.Z);
    double[] scale = [d.Item1 / s.Item1, 0, 0, 0, d.Item2 / s.Item2, 0, 0, 0, d.Item3 / s.Item3];
    return Multiply(inverse, Multiply(scale, cone));
  }
}

/// <summary>A tone curve: a parametric function evaluated in floating point, or a 16-bit table.</summary>
internal sealed class IccToneCurve {
  private readonly int _type;
  private readonly double[] _parameters = [];
  private readonly ushort[]? _table;

  private IccToneCurve(int type, double[] parameters, ushort[]? table) {
    this._type = type;
    this._parameters = parameters;
    this._table = table;
  }

  /// <summary>A Little CMS parametric curve: types 1–5 are ICC function types 0–4, negative types their inverses.</summary>
  public static IccToneCurve Parametric(int type, double[] parameters) => new(type, parameters, null);

  public static IccToneCurve Table(ushort[] table) => new(0, [], table);

  /// <summary>cmsEvalToneCurveFloat.</summary>
  public double Evaluate(double v) {
    if (this._table != null)
      return LinLerp1D(this._table, IccMath.SaturateWord(v * 65535.0)) / 65535.0;
    return _Parametric(this._type, this._parameters, v);
  }

  /// <summary>LinLerp1D of cmsintrp.c.</summary>
  public static ushort LinLerp1D(ushort[] table, int value) {
    var domain = table.Length - 1;
    if (value == 0xFFFF || domain == 0)
      return table[domain];

    var fixedValue = IccMath.ToFixedDomain(domain * value);
    var cell = fixedValue >> 16;
    var rest = fixedValue & 0xFFFF;
    return IccMath.LinearInterp(rest, table[cell], table[cell + 1]);
  }

  /// <summary>DefaultEvalParametricFn of cmsgamma.c, for the types this needs.</summary>
  private static double _Parametric(int type, double[] p, double r) {
    const double tolerance = IccMath.DeterminantTolerance;
    double e;
    switch (type) {
      case 1:
        if (r < 0)
          return Math.Abs(p[0] - 1.0) < tolerance ? r : 0;
        return Math.Pow(r, p[0]);
      case 2: {
        if (Math.Abs(p[1]) < tolerance)
          return 0;
        var disc = -p[2] / p[1];
        if (r < disc)
          return 0;
        e = p[1] * r + p[2];
        return e > 0 ? Math.Pow(e, p[0]) : 0;
      }
      case 3: {
        if (Math.Abs(p[1]) < tolerance)
          return 0;
        var disc = Math.Max(0, -p[2] / p[1]);
        if (r < disc)
          return p[3];
        e = p[1] * r + p[2];
        return e > 0 ? Math.Pow(e, p[0]) + p[3] : 0;
      }
      case 4:
        if (r >= p[4]) {
          e = p[1] * r + p[2];
          return e > 0 ? Math.Pow(e, p[0]) : 0;
        }

        return r * p[3];
      case -4: {
        e = p[1] * p[4] + p[2];
        var disc = e < 0 ? 0 : Math.Pow(e, p[0]);
        if (r >= disc) {
          if (Math.Abs(p[0]) < tolerance || Math.Abs(p[1]) < tolerance)
            return 0;
          return (Math.Pow(r, 1.0 / p[0]) - p[2]) / p[1];
        }

        return Math.Abs(p[3]) < tolerance ? 0 : r / p[3];
      }
      case 5:
        if (r >= p[4]) {
          e = p[1] * r + p[2];
          return e > 0 ? Math.Pow(e, p[0]) + p[5] : p[5];
        }

        return r * p[3] + p[6];
      default:
        return r;
    }
  }
}

/// <summary>A processing stage of a pipeline, over values in Little CMS's 0..1 encodings, stored at single precision as it stores them.</summary>
internal abstract class IccStage {
  public abstract int Inputs { get; }
  public abstract int Outputs { get; }
  public abstract void Evaluate(ReadOnlySpan<float> input, Span<float> output);

  public static IccStage Curves(IccToneCurve[] curves) => new CurveStage(curves);

  public static IccStage Matrix(int inputs, int outputs, double[] m, double[]? offset) => new MatrixStage(inputs, outputs, m, offset);

  public static IccStage Scale(int n, double factor) {
    var m = new double[n * n];
    for (var i = 0; i < n; ++i)
      m[i * n + i] = factor;
    return new MatrixStage(n, n, m, null);
  }

  public static IccStage LabToXyz() => new LabToXyzStage();

  public static IccStage XyzToLab() => new XyzToLabStage();

  private sealed class CurveStage(IccToneCurve[] curves) : IccStage {
    public override int Inputs => curves.Length;
    public override int Outputs => curves.Length;

    public override void Evaluate(ReadOnlySpan<float> input, Span<float> output) {
      for (var i = 0; i < curves.Length; ++i)
        output[i] = (float)curves[i].Evaluate(input[i]);
    }
  }

  internal sealed class MatrixStage(int inputs, int outputs, double[] m, double[]? offset) : IccStage {
    public override int Inputs => inputs;
    public override int Outputs => outputs;
    public double[] Values => m;
    public double[]? Offset => offset;

    public override void Evaluate(ReadOnlySpan<float> input, Span<float> output) {
      for (var i = 0; i < outputs; ++i) {
        double sum = 0;
        for (var j = 0; j < inputs; ++j)
          sum += input[j] * m[i * inputs + j];
        if (offset != null)
          sum += offset[i];
        output[i] = (float)sum;
      }
    }
  }

  private sealed class LabToXyzStage : IccStage {
    public override int Inputs => 3;
    public override int Outputs => 3;

    public override void Evaluate(ReadOnlySpan<float> input, Span<float> output) {
      var (x, y, z) = IccMath.LabToXyz(input[0] * 100.0, input[1] * 255.0 - 128.0, input[2] * 255.0 - 128.0);
      output[0] = (float)(x / IccMath.MaxEncodeableXyz);
      output[1] = (float)(y / IccMath.MaxEncodeableXyz);
      output[2] = (float)(z / IccMath.MaxEncodeableXyz);
    }
  }

  private sealed class XyzToLabStage : IccStage {
    public override int Inputs => 3;
    public override int Outputs => 3;

    public override void Evaluate(ReadOnlySpan<float> input, Span<float> output) {
      var (l, a, b) = IccMath.XyzToLab(input[0] * IccMath.MaxEncodeableXyz, input[1] * IccMath.MaxEncodeableXyz, input[2] * IccMath.MaxEncodeableXyz);
      output[0] = (float)(l / 100.0);
      output[1] = (float)((a + 128.0) / 255.0);
      output[2] = (float)((b + 128.0) / 255.0);
    }
  }

  /// <summary>
  /// A 16-bit colour lookup table evaluated the way EvaluateCLUTfloatIn16 does: inputs rounded to
  /// 16 bits, interpolated in fixed point (cmsintrp.c: LinLerp1D, Eval1Input, BilinearInterp16,
  /// TetrahedralInterp16 or TrilinearInterp16, and Eval4Inputs…Eval15Inputs above three), outputs back
  /// to 0..1.
  /// </summary>
  internal sealed class ClutStage : IccStage {
    private readonly int[] _grid;
    private readonly int _outputs;
    private readonly ushort[] _table;
    private readonly int[] _opta;

    public ClutStage(int[] grid, int outputs, ushort[] table) {
      this._grid = grid;
      this._outputs = outputs;
      this._table = table;
      this._opta = new int[grid.Length];
      this._opta[0] = outputs;
      for (var i = 1; i < grid.Length; ++i)
        this._opta[i] = this._opta[i - 1] * grid[grid.Length - i];
    }

    public bool Trilinear { get; set; }
    public override int Inputs => this._grid.Length;
    public override int Outputs => this._outputs;

    public override void Evaluate(ReadOnlySpan<float> input, Span<float> output) {
      var n = this._grid.Length;
      Span<ushort> in16 = stackalloc ushort[n];
      Span<ushort> out16 = stackalloc ushort[this._outputs];
      for (var i = 0; i < n; ++i)
        in16[i] = IccMath.SaturateWord(input[i] * 65535.0);

      this._Interpolate(in16, out16, 0, 0);
      for (var i = 0; i < this._outputs; ++i)
        output[i] = (float)(out16[i] / 65535.0);
    }

    /// <summary>Interpolates the inputs from <paramref name="first"/> on, in the sub-table at <paramref name="baseIndex"/>.</summary>
    private void _Interpolate(ReadOnlySpan<ushort> input, Span<ushort> output, int first, int baseIndex) {
      var remaining = this._grid.Length - first;
      switch (remaining) {
        case 1:
          this._One(input[first], output, first, baseIndex);
          return;
        case 2:
          this._Bilinear(input[first], input[first + 1], output, first, baseIndex);
          return;
        case 3:
          if (this.Trilinear && first == 0)
            this._Trilinear(input, output, baseIndex);
          else
            this._Tetrahedral(input[first], input[first + 1], input[first + 2], output, first, baseIndex);
          return;
      }

      // Eval4Inputs … Eval15Inputs: two interpolations one dimension down, then linearly between them.
      var domain = this._grid[first] - 1;
      var fk = IccMath.ToFixedDomain(input[first] * domain);
      var k0 = fk >> 16;
      var rk = fk & 0xFFFF;
      var opta = this._opta[remaining - 1];
      var k1 = k0 + (input[first] != 0xFFFF ? 1 : 0);
      Span<ushort> t1 = stackalloc ushort[this._outputs];
      Span<ushort> t2 = stackalloc ushort[this._outputs];
      this._Interpolate(input, t1, first + 1, baseIndex + opta * k0);
      this._Interpolate(input, t2, first + 1, baseIndex + opta * k1);
      for (var i = 0; i < this._outputs; ++i)
        output[i] = IccMath.LinearInterp(rk, t1[i], t2[i]);
    }

    private void _One(ushort value, Span<ushort> output, int first, int baseIndex) {
      var domain = this._grid[first] - 1;
      var opta = this._opta[0];
      if (value == 0xFFFF || domain == 0) {
        var y0 = baseIndex + domain * opta;
        for (var o = 0; o < this._outputs; ++o)
          output[o] = this._table[y0 + o];
        return;
      }

      var fk = IccMath.ToFixedDomain(value * domain);
      var k0 = fk >> 16;
      var rk = fk & 0xFFFF;
      var kk0 = baseIndex + opta * k0;
      var kk1 = baseIndex + opta * (k0 + 1);
      for (var o = 0; o < this._outputs; ++o)
        output[o] = IccMath.LinearInterp(rk, this._table[kk0 + o], this._table[kk1 + o]);
    }

    private static int _Lerp(int a, int l, int h) => (ushort)(l + (((h - l) * a + 0x8000) >> 16));

    private void _Bilinear(ushort i0, ushort i1, Span<ushort> output, int first, int baseIndex) {
      var fx = IccMath.ToFixedDomain(i0 * (this._grid[first] - 1));
      var x0 = fx >> 16;
      var rx = fx & 0xFFFF;
      var fy = IccMath.ToFixedDomain(i1 * (this._grid[first + 1] - 1));
      var y0 = fy >> 16;
      var ry = fy & 0xFFFF;
      var xx0 = this._opta[1] * x0;
      var xx1 = xx0 + (i0 == 0xFFFF ? 0 : this._opta[1]);
      var yy0 = this._opta[0] * y0;
      var yy1 = yy0 + (i1 == 0xFFFF ? 0 : this._opta[0]);
      var t = this._table;
      for (var o = 0; o < this._outputs; ++o) {
        var b = baseIndex + o;
        var dx0 = _Lerp(rx, t[b + xx0 + yy0], t[b + xx1 + yy0]);
        var dx1 = _Lerp(rx, t[b + xx0 + yy1], t[b + xx1 + yy1]);
        output[o] = (ushort)_Lerp(ry, dx0, dx1);
      }
    }

    private void _Trilinear(ReadOnlySpan<ushort> input, Span<ushort> output, int baseIndex) {
      var fx = IccMath.ToFixedDomain(input[0] * (this._grid[0] - 1));
      var fy = IccMath.ToFixedDomain(input[1] * (this._grid[1] - 1));
      var fz = IccMath.ToFixedDomain(input[2] * (this._grid[2] - 1));
      int rx = fx & 0xFFFF, ry = fy & 0xFFFF, rz = fz & 0xFFFF;
      var xx0 = this._opta[2] * (fx >> 16);
      var xx1 = xx0 + (input[0] == 0xFFFF ? 0 : this._opta[2]);
      var yy0 = this._opta[1] * (fy >> 16);
      var yy1 = yy0 + (input[1] == 0xFFFF ? 0 : this._opta[1]);
      var zz0 = this._opta[0] * (fz >> 16);
      var zz1 = zz0 + (input[2] == 0xFFFF ? 0 : this._opta[0]);
      var t = this._table;
      for (var o = 0; o < this._outputs; ++o) {
        var b = baseIndex + o;
        var dx00 = _Lerp(rx, t[b + xx0 + yy0 + zz0], t[b + xx1 + yy0 + zz0]);
        var dx01 = _Lerp(rx, t[b + xx0 + yy0 + zz1], t[b + xx1 + yy0 + zz1]);
        var dx10 = _Lerp(rx, t[b + xx0 + yy1 + zz0], t[b + xx1 + yy1 + zz0]);
        var dx11 = _Lerp(rx, t[b + xx0 + yy1 + zz1], t[b + xx1 + yy1 + zz1]);
        var dxy0 = _Lerp(ry, dx00, dx10);
        var dxy1 = _Lerp(ry, dx01, dx11);
        output[o] = (ushort)_Lerp(rz, dxy0, dxy1);
      }
    }

    private void _Tetrahedral(ushort i0, ushort i1, ushort i2, Span<ushort> output, int first, int baseIndex) {
      var fx = IccMath.ToFixedDomain(i0 * (this._grid[first] - 1));
      var fy = IccMath.ToFixedDomain(i1 * (this._grid[first + 1] - 1));
      var fz = IccMath.ToFixedDomain(i2 * (this._grid[first + 2] - 1));
      int rx = fx & 0xFFFF, ry = fy & 0xFFFF, rz = fz & 0xFFFF;
      var x0 = this._opta[2] * (fx >> 16);
      var x1 = i0 == 0xFFFF ? 0 : this._opta[2];
      var y0 = this._opta[1] * (fy >> 16);
      var y1 = i1 == 0xFFFF ? 0 : this._opta[1];
      var z0 = this._opta[0] * (fz >> 16);
      var z1 = i2 == 0xFFFF ? 0 : this._opta[0];
      var t = this._table;
      var origin = baseIndex + x0 + y0 + z0;
      for (var o = 0; o < this._outputs; ++o) {
        var b = origin + o;
        int c0 = t[b], c1, c2, c3;
        if (rx >= ry) {
          if (ry >= rz) {
            c1 = t[b + x1] - c0;
            c2 = t[b + x1 + y1] - t[b + x1];
            c3 = t[b + x1 + y1 + z1] - t[b + x1 + y1];
          } else if (rz >= rx) {
            c1 = t[b + x1 + z1] - t[b + z1];
            c2 = t[b + x1 + y1 + z1] - t[b + x1 + z1];
            c3 = t[b + z1] - c0;
          } else {
            c1 = t[b + x1] - c0;
            c2 = t[b + x1 + y1 + z1] - t[b + x1 + z1];
            c3 = t[b + x1 + z1] - t[b + x1];
          }
        } else if (rx >= rz) {
          c1 = t[b + x1 + y1] - t[b + y1];
          c2 = t[b + y1] - c0;
          c3 = t[b + x1 + y1 + z1] - t[b + x1 + y1];
        } else if (ry >= rz) {
          c1 = t[b + x1 + y1 + z1] - t[b + y1 + z1];
          c2 = t[b + y1] - c0;
          c3 = t[b + y1 + z1] - t[b + y1];
        } else {
          c1 = t[b + x1 + y1 + z1] - t[b + y1 + z1];
          c2 = t[b + y1 + z1] - t[b + z1];
          c3 = t[b + z1] - c0;
        }

        var rest = c1 * rx + c2 * ry + c3 * rz + 0x8001;
        output[o] = (ushort)(c0 + ((rest + (rest >> 16)) >> 16));
      }
    }
  }
}

/// <summary>A sequence of stages (cmsPipeline), evaluated in single precision as _LUTevalFloat does.</summary>
internal sealed class IccPipeline(int inputs, int outputs) {
  public int Inputs { get; } = inputs;
  public int Outputs { get; } = outputs;
  public List<IccStage> Stages { get; } = [];

  public void Evaluate(ReadOnlySpan<float> input, Span<float> output) {
    Span<float> a = stackalloc float[IccMath.MaxChannels + 1];
    Span<float> b = stackalloc float[IccMath.MaxChannels + 1];
    input.CopyTo(a);
    var count = input.Length;
    foreach (var stage in this.Stages) {
      stage.Evaluate(a[..stage.Inputs], b);
      count = stage.Outputs;
      var swap = a;
      a = b;
      b = swap;
    }

    a[..Math.Min(count, output.Length)].CopyTo(output);
  }
}
