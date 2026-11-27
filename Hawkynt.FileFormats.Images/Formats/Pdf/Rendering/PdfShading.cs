using System;
using System.Collections.Generic;
using FileFormat.Core.Vector;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// A shading (PDF 32000-1 §8.7.4.5): a colour that varies over an area, as a paint for the device.
/// </summary>
/// <remarks>
/// Written from the specification (rung 3). Function, axial and radial shadings are evaluated per
/// device pixel through the inverse of the shading's transform, with the colour function sampled
/// once into a table along the parameter for the two that have one input. The mesh shadings — free
/// form and lattice Gouraud triangles, Coons and tensor-product patches — are decoded from their
/// streams, the patches subdivided into small bilinear quads, and the triangles filled with
/// interpolated colour onto a raster the size of the area they reach; that raster is then the paint.
/// </remarks>
internal abstract class PdfShading {

  protected PdfColorModel Space = PdfColorModel.DeviceGray;
  protected PdfFunction? Function;
  protected double[]? Background;
  protected double[]? BoundingBox;

  /// <summary>Reads a shading dictionary or stream.</summary>
  public static PdfShading Parse(PdfDocument document, object? value, PdfDict? resources) {
    var resolved = document.Resolve(value);
    var dict = document.Dict(resolved) ?? throw new NotSupportedException("A shading is neither a dictionary nor a stream.");
    var type = document.Integer(dict, "ShadingType");
    PdfShading shading = type switch {
      1 => new FunctionShading(),
      2 => new AxialShading(),
      3 => new RadialShading(),
      4 or 5 or 6 or 7 => new MeshShading(type),
      _ => throw new NotSupportedException($"Shading type {type} is not supported."),
    };

    shading.Space = PdfColorModel.Parse(document, dict.Raw("ColorSpace"), resources);
    if (shading.Space.IsPattern)
      throw new NotSupportedException("A shading in the Pattern colour space is not valid.");
    if (dict.Raw("Function") is { } function) {
      shading.Function = PdfFunction.Parse(document, function);

      // A function taking the wrong number of inputs makes the shading invalid; poppler draws
      // nothing for it, and neither does this.
      var expected = type == 1 ? 2 : 1;
      if (shading.Function.Inputs != expected)
        return new EmptyShading();
    }
    shading.Background = document.Numbers(dict.Raw("Background"));
    shading.BoundingBox = document.Numbers(dict.Raw("BBox")) is { Length: 4 } box ? box : null;
    shading.Load(document, dict, resolved as PdfStreamObject);
    return shading;
  }

  protected abstract void Load(PdfDocument document, PdfDict dict, PdfStreamObject? stream);

  /// <summary>A paint for the shading placed by a transform from shading space to the device.</summary>
  /// <param name="useBackground">Whether areas the shading does not reach get its /Background, as they do in a pattern.</param>
  public abstract PdfPaint CreatePaint(Matrix2D toDevice, PixelBox area, bool useBackground);

  /// <summary>The device box the shading can reach, or null for everywhere (an extended or function shading).</summary>
  public virtual PixelBox? Extent(Matrix2D toDevice, int width, int height) => null;

  /// <summary>The colour for a set of components (or a parameter fed through the function).</summary>
  protected void ToRgb(ReadOnlySpan<double> input, Span<double> rgb) {
    if (this.Function == null) {
      this.Space.ToRgb(input, rgb);
      return;
    }

    Span<double> components = stackalloc double[Math.Max(this.Space.Components, this.Function.Outputs) + 1];
    components.Clear();
    this.Function.Evaluate(input, components);
    this.Space.ToRgb(components, rgb);
  }

  protected (float R, float G, float B)? BackgroundRgb() {
    if (this.Background == null || this.Background.Length < this.Space.Components)
      return null;

    Span<double> rgb = stackalloc double[3];
    this.Space.ToRgb(this.Background, rgb);
    return ((float)rgb[0], (float)rgb[1], (float)rgb[2]);
  }

  /// <summary>Whether a shading-space point is inside the /BBox, when there is one.</summary>
  protected bool InBox(double x, double y) {
    var b = this.BoundingBox;
    return b == null || (x >= Math.Min(b[0], b[2]) && x <= Math.Max(b[0], b[2]) && y >= Math.Min(b[1], b[3]) && y <= Math.Max(b[1], b[3]));
  }

  /// <summary>A one-input colour table along the parameter, from t0 to t1.</summary>
  protected float[] Table(double t0, double t1, int size) {
    var table = new float[size * 3];
    Span<double> rgb = stackalloc double[3];
    Span<double> input = stackalloc double[1];
    for (var i = 0; i < size; ++i) {
      input[0] = t0 + (t1 - t0) * i / (size - 1);
      this.ToRgb(input, rgb);
      table[i * 3] = (float)Math.Clamp(rgb[0], 0, 1);
      table[i * 3 + 1] = (float)Math.Clamp(rgb[1], 0, 1);
      table[i * 3 + 2] = (float)Math.Clamp(rgb[2], 0, 1);
    }

    return table;
  }

  /// <summary>The colour at a shading-space point, or false where the shading does not reach.</summary>
  protected delegate bool ColorAt(double x, double y, Span<float> color);

  /// <summary>Per-pixel paints share this: map a device pixel to shading space and ask the subclass.</summary>
  private sealed class PointPaint(PdfShading owner, Matrix2D toShading, ColorAt colorAt, (float R, float G, float B)? background) : PdfPaint {
    public override void Fill(int y, int x0, int count, Span<float> rgb, Span<float> alpha) {
      Span<float> c = stackalloc float[3];
      for (var i = 0; i < count; ++i) {
        if (alpha[i] <= 0)
          continue;

        var (sx, sy) = toShading.Apply(x0 + i + 0.5, y + 0.5);
        if (owner.InBox(sx, sy) && colorAt(sx, sy, c)) {
          rgb[i * 3] = c[0];
          rgb[i * 3 + 1] = c[1];
          rgb[i * 3 + 2] = c[2];
        } else if (background is { } bg && owner.InBox(sx, sy)) {
          rgb[i * 3] = bg.R;
          rgb[i * 3 + 1] = bg.G;
          rgb[i * 3 + 2] = bg.B;
        } else
          alpha[i] = 0;
      }
    }
  }

  protected PdfPaint PointwisePaint(Matrix2D toDevice, ColorAt colorAt, bool useBackground) {
    var inverse = toDevice.Inverse ?? new Matrix2D(0, 0, 0, 0, double.NaN, double.NaN);
    return new PointPaint(this, inverse, colorAt, useBackground ? this.BackgroundRgb() : null);
  }

  /// <summary>A shading that is invalid and paints nothing.</summary>
  private sealed class EmptyShading : PdfShading {
    protected override void Load(PdfDocument document, PdfDict dict, PdfStreamObject? stream) { }
    public override PdfPaint CreatePaint(Matrix2D toDevice, PixelBox area, bool useBackground) => new NothingPaint();
    public override PixelBox? Extent(Matrix2D toDevice, int width, int height) => new PixelBox(0, 0, 0, 0);

    private sealed class NothingPaint : PdfPaint {
      public override void Fill(int y, int x0, int count, Span<float> rgb, Span<float> alpha) => alpha[..count].Clear();
    }
  }

  private sealed class FunctionShading : PdfShading {
    private double[] _domain = [0, 1, 0, 1];
    private Matrix2D _matrix = Matrix2D.Identity;

    protected override void Load(PdfDocument document, PdfDict dict, PdfStreamObject? stream) {
      if (this.Function == null)
        throw new NotSupportedException("A function shading has no /Function.");
      if (document.Numbers(dict.Raw("Domain")) is { Length: 4 } domain)
        this._domain = domain;
      if (document.Numbers(dict.Raw("Matrix")) is { Length: 6 } m)
        this._matrix = new Matrix2D(m[0], m[1], m[2], m[3], m[4], m[5]);
    }

    public override PdfPaint CreatePaint(Matrix2D toDevice, PixelBox area, bool useBackground) {
      // /BBox is in shading space; /Matrix takes the function's domain into it.
      var toDomain = this._matrix.Inverse ?? new Matrix2D(0, 0, 0, 0, double.NaN, double.NaN);
      var cache = new Dictionary<long, (float, float, float)>();
      return this.PointwisePaint(toDevice, (sx, sy, c) => {
        var (x, y) = toDomain.Apply(sx, sy);
        if (x < this._domain[0] || x > this._domain[1] || y < this._domain[2] || y > this._domain[3])
          return false;

        // Neighbouring pixels map to nearby points; quantising to a fine grid keeps the function from
        // being run once per pixel when it is expensive (a PostScript program, a sampled table).
        var qx = (long)Math.Round((x - this._domain[0]) / Math.Max(1e-12, this._domain[1] - this._domain[0]) * 2048);
        var qy = (long)Math.Round((y - this._domain[2]) / Math.Max(1e-12, this._domain[3] - this._domain[2]) * 2048);
        var key = qx * 4096 + qy;
        if (!cache.TryGetValue(key, out var hit)) {
          Span<double> rgb = stackalloc double[3];
          this.ToRgb([x, y], rgb);
          hit = ((float)Math.Clamp(rgb[0], 0, 1), (float)Math.Clamp(rgb[1], 0, 1), (float)Math.Clamp(rgb[2], 0, 1));
          if (cache.Count < 1 << 20)
            cache[key] = hit;
        }

        (c[0], c[1], c[2]) = hit;
        return true;
      }, useBackground);
    }
  }

  private sealed class AxialShading : PdfShading {
    private double _x0, _y0, _x1, _y1, _t0, _t1 = 1;
    private bool _extendStart, _extendEnd;
    private float[] _table = [];

    protected override void Load(PdfDocument document, PdfDict dict, PdfStreamObject? stream) {
      var coords = document.Numbers(dict.Raw("Coords")) ?? throw new NotSupportedException("An axial shading has no /Coords.");
      if (coords.Length < 4 || this.Function == null)
        throw new NotSupportedException("An axial shading needs four /Coords and a /Function.");
      (this._x0, this._y0, this._x1, this._y1) = (coords[0], coords[1], coords[2], coords[3]);
      if (document.Numbers(dict.Raw("Domain")) is { Length: 2 } domain)
        (this._t0, this._t1) = (domain[0], domain[1]);
      if (document.Array(dict.Raw("Extend")) is { Count: 2 } extend) {
        this._extendStart = document.Resolve(extend[0]) is true;
        this._extendEnd = document.Resolve(extend[1]) is true;
      }

      this._table = this.Table(this._t0, this._t1, 1024);
    }

    public override PdfPaint CreatePaint(Matrix2D toDevice, PixelBox area, bool useBackground) {
      var dx = this._x1 - this._x0;
      var dy = this._y1 - this._y0;
      var length = dx * dx + dy * dy;
      return this.PointwisePaint(toDevice, (x, y, c) => {
        var s = length <= 0 ? 0 : ((x - this._x0) * dx + (y - this._y0) * dy) / length;
        if (s < 0) {
          if (!this._extendStart)
            return false;
          s = 0;
        } else if (s > 1) {
          if (!this._extendEnd)
            return false;
          s = 1;
        }

        var at = (int)Math.Round(s * 1023) * 3;
        c[0] = this._table[at];
        c[1] = this._table[at + 1];
        c[2] = this._table[at + 2];
        return true;
      }, useBackground);
    }
  }

  private sealed class RadialShading : PdfShading {
    private double _x0, _y0, _r0, _x1, _y1, _r1, _t0, _t1 = 1;
    private bool _extendStart, _extendEnd;
    private float[] _table = [];

    protected override void Load(PdfDocument document, PdfDict dict, PdfStreamObject? stream) {
      var coords = document.Numbers(dict.Raw("Coords")) ?? throw new NotSupportedException("A radial shading has no /Coords.");
      if (coords.Length < 6 || this.Function == null)
        throw new NotSupportedException("A radial shading needs six /Coords and a /Function.");
      (this._x0, this._y0, this._r0, this._x1, this._y1, this._r1) = (coords[0], coords[1], coords[2], coords[3], coords[4], coords[5]);
      if (document.Numbers(dict.Raw("Domain")) is { Length: 2 } domain)
        (this._t0, this._t1) = (domain[0], domain[1]);
      if (document.Array(dict.Raw("Extend")) is { Count: 2 } extend) {
        this._extendStart = document.Resolve(extend[0]) is true;
        this._extendEnd = document.Resolve(extend[1]) is true;
      }

      this._table = this.Table(this._t0, this._t1, 1024);
    }

    public override PdfPaint CreatePaint(Matrix2D toDevice, PixelBox area, bool useBackground) {
      var cdx = this._x1 - this._x0;
      var cdy = this._y1 - this._y0;
      var dr = this._r1 - this._r0;
      var a = cdx * cdx + cdy * cdy - dr * dr;
      return this.PointwisePaint(toDevice, (x, y, c) => {
        // The circles are c(s) = c0 + s·(c1 − c0), r(s) = r0 + s·(r1 − r0); a point takes the
        // largest s whose circle passes through it with r(s) ≥ 0 (§8.7.4.5.4).
        var px = x - this._x0;
        var py = y - this._y0;
        var b = px * cdx + py * cdy + this._r0 * dr;
        var cc = px * px + py * py - this._r0 * this._r0;
        double s;
        if (Math.Abs(a) < 1e-12) {
          if (Math.Abs(b) < 1e-12)
            return false;
          s = cc / (2 * b);
          if (!this._Accept(ref s))
            return false;
        } else {
          var disc = b * b - a * cc;
          if (disc < 0)
            return false;
          var root = Math.Sqrt(disc);
          var s1 = (b + root) / a;
          var s2 = (b - root) / a;
          var hi = Math.Max(s1, s2);
          var lo = Math.Min(s1, s2);
          s = hi;
          if (!this._Accept(ref s)) {
            s = lo;
            if (!this._Accept(ref s))
              return false;
          }
        }

        var at = (int)Math.Round(s * 1023) * 3;
        c[0] = this._table[at];
        c[1] = this._table[at + 1];
        c[2] = this._table[at + 2];
        return true;
      }, useBackground);
    }

    private bool _Accept(ref double s) {
      if (this._r0 + s * (this._r1 - this._r0) < 0)
        return false;
      if (s < 0) {
        if (!this._extendStart)
          return false;
        s = 0;
      } else if (s > 1) {
        if (!this._extendEnd)
          return false;
        s = 1;
      }

      return true;
    }
  }

  /// <summary>Types 4 to 7: triangles and patches with colour at their vertices.</summary>
  private sealed class MeshShading(int type) : PdfShading {
    private readonly List<(double X, double Y, double[] C)> _vertices = [];
    private readonly List<(int A, int B, int C)> _triangles = [];
    private int _components;

    protected override void Load(PdfDocument document, PdfDict dict, PdfStreamObject? stream) {
      if (stream == null)
        throw new NotSupportedException($"A type {type} shading must be a stream.");

      var data = document.Decode(stream);
      var bitsCoord = document.Integer(dict, "BitsPerCoordinate");
      var bitsComp = document.Integer(dict, "BitsPerComponent");
      var bitsFlag = document.Integer(dict, "BitsPerFlag", 8);
      var decode = document.Numbers(dict.Raw("Decode")) ?? throw new NotSupportedException("A mesh shading has no /Decode.");
      this._components = this.Function != null ? 1 : this.Space.Components;
      if (decode.Length < 4 + 2 * this._components)
        throw new NotSupportedException("A mesh shading's /Decode is too short.");
      if (bitsCoord is < 1 or > 32 || bitsComp is < 1 or > 16)
        throw new NotSupportedException("A mesh shading's bit depths are out of range.");

      var reader = new BitReader(data);
      switch (type) {
        case 4:
          this._FreeForm(reader, bitsCoord, bitsComp, bitsFlag, decode);
          break;
        case 5:
          this._Lattice(reader, bitsCoord, bitsComp, decode, document.Integer(dict, "VerticesPerRow"));
          break;
        default:
          this._Patches(reader, bitsCoord, bitsComp, bitsFlag, decode, type == 7);
          break;
      }
    }

    private (double X, double Y, double[] C) _ReadVertex(BitReader reader, int bitsCoord, int bitsComp, double[] decode) {
      var x = _Map(reader.Read(bitsCoord), bitsCoord, decode[0], decode[1]);
      var y = _Map(reader.Read(bitsCoord), bitsCoord, decode[2], decode[3]);
      var c = new double[this._components];
      for (var i = 0; i < this._components; ++i)
        c[i] = _Map(reader.Read(bitsComp), bitsComp, decode[4 + 2 * i], decode[5 + 2 * i]);
      return (x, y, c);
    }

    private static double _Map(ulong value, int bits, double min, double max) => min + value * (max - min) / ((1UL << bits) - 1);

    private void _FreeForm(BitReader reader, int bitsCoord, int bitsComp, int bitsFlag, double[] decode) {
      // Each vertex is padded to a whole byte. A flag of 0 starts a triangle from this vertex and the
      // next two; 1 and 2 add a vertex to the last triangle's (b, c) or (a, c) edge (§8.7.4.5.5).
      int a = -1, b = -1, c = -1;
      var pending = new List<int>(3);
      var awaiting = 0;
      while (reader.HasBits(bitsFlag + 2 * bitsCoord + this._components * bitsComp)) {
        var flag = (int)reader.Read(bitsFlag);
        this._vertices.Add(this._ReadVertex(reader, bitsCoord, bitsComp, decode));
        reader.AlignToByte();
        var index = this._vertices.Count - 1;

        if (awaiting > 0) {
          pending.Add(index);
          if (--awaiting == 0) {
            (a, b, c) = (pending[0], pending[1], pending[2]);
            this._triangles.Add((a, b, c));
          }

          continue;
        }

        switch (flag) {
          case 0:
            pending.Clear();
            pending.Add(index);
            awaiting = 2;
            break;
          case 1 when b >= 0:
            (a, b, c) = (b, c, index);
            this._triangles.Add((a, b, c));
            break;
          case 2 when a >= 0:
            (a, b, c) = (a, c, index);
            this._triangles.Add((a, b, c));
            break;
        }
      }
    }

    private void _Lattice(BitReader reader, int bitsCoord, int bitsComp, double[] decode, int perRow) {
      if (perRow < 2)
        throw new NotSupportedException("A lattice shading needs at least two vertices a row.");

      while (reader.HasBits(2 * bitsCoord + this._components * bitsComp))
        this._vertices.Add(this._ReadVertex(reader, bitsCoord, bitsComp, decode));

      var rows = this._vertices.Count / perRow;
      for (var r = 0; r + 1 < rows; ++r)
        for (var k = 0; k + 1 < perRow; ++k) {
          var i = r * perRow + k;
          this._triangles.Add((i, i + 1, i + perRow));
          this._triangles.Add((i + 1, i + perRow + 1, i + perRow));
        }
    }

    private void _Patches(BitReader reader, int bitsCoord, int bitsComp, int bitsFlag, double[] decode, bool tensor) {
      var points = new (double X, double Y)[16];
      var colors = new double[4][];
      var previous = new (double X, double Y)[16];
      var previousColors = new double[4][];
      var havePrevious = false;
      var pointCount = tensor ? 16 : 12;

      while (reader.HasBits(bitsFlag)) {
        var flag = (int)reader.Read(bitsFlag);
        var newPoints = flag == 0 ? pointCount : pointCount - 4;
        var newColors = flag == 0 ? 4 : 2;
        if (!reader.HasBits(newPoints * 2 * bitsCoord + newColors * this._components * bitsComp))
          break;
        if (flag != 0 && !havePrevious)
          break;

        var read = new (double X, double Y)[newPoints];
        for (var i = 0; i < newPoints; ++i)
          read[i] = (_Map(reader.Read(bitsCoord), bitsCoord, decode[0], decode[1]), _Map(reader.Read(bitsCoord), bitsCoord, decode[2], decode[3]));
        var readColors = new double[newColors][];
        for (var i = 0; i < newColors; ++i) {
          readColors[i] = new double[this._components];
          for (var k = 0; k < this._components; ++k)
            readColors[i][k] = _Map(reader.Read(bitsComp), bitsComp, decode[4 + 2 * k], decode[5 + 2 * k]);
        }

        reader.AlignToByte();

        // Points are listed around the boundary (§8.7.4.5.7, figure 46); in the grid below,
        // P[i,j] with i across and j up. A shared edge carries its four points over from the
        // previous patch's side named by the flag, in reverse.
        var boundary = new (double X, double Y)[12];
        var corner = new double[4][];
        if (flag == 0) {
          Array.Copy(read, boundary, 12);
          Array.Copy(readColors, corner, 4);
        } else {
          var (shared, sharedColors) = flag switch {
            1 => (new[] { previous[3], previous[4], previous[5], previous[6] }, new[] { previousColors[1], previousColors[2] }),
            2 => (new[] { previous[6], previous[7], previous[8], previous[9] }, new[] { previousColors[2], previousColors[3] }),
            _ => (new[] { previous[9], previous[10], previous[11], previous[0] }, new[] { previousColors[3], previousColors[0] }),
          };
          for (var i = 0; i < 4; ++i)
            boundary[i] = shared[i];
          for (var i = 0; i < 8; ++i)
            boundary[4 + i] = read[i];
          corner[0] = sharedColors[0];
          corner[1] = sharedColors[1];
          corner[2] = readColors[0];
          corner[3] = readColors[1];
        }

        var interior = new (double X, double Y)[4];
        if (tensor) {
          var extra = flag == 0 ? read[12..16] : read[8..12];
          interior = extra;
        }

        for (var i = 0; i < 12; ++i)
          previous[i] = boundary[i];
        for (var i = 0; i < 4; ++i)
          previousColors[i] = corner[i];
        havePrevious = true;

        this._AddPatch(boundary, interior, tensor, corner);
      }
    }

    /// <summary>Evaluates a Coons or tensor patch on a grid and adds its triangles.</summary>
    private void _AddPatch((double X, double Y)[] b, (double X, double Y)[] interior, bool tensor, double[][] corner) {
      // The 4×4 control grid P[i][j] (i = u index, j = v index) of the tensor form.
      var p = new (double X, double Y)[4, 4];
      p[0, 0] = b[0]; p[0, 1] = b[1]; p[0, 2] = b[2]; p[0, 3] = b[3];
      p[1, 3] = b[4]; p[2, 3] = b[5]; p[3, 3] = b[6];
      p[3, 2] = b[7]; p[3, 1] = b[8]; p[3, 0] = b[9];
      p[2, 0] = b[10]; p[1, 0] = b[11];
      if (tensor) {
        // Tensor patches list the four inner points as P11, P12, P22, P21.
        p[1, 1] = interior[0];
        p[1, 2] = interior[1];
        p[2, 2] = interior[2];
        p[2, 1] = interior[3];
      } else {
        // A Coons patch's inner points follow from its boundary (§8.7.4.5.8).
        p[1, 1] = _Coons(p[0, 0], p[0, 1], p[1, 0], p[0, 3], p[3, 0], p[3, 1], p[1, 3], p[3, 3]);
        p[1, 2] = _Coons(p[0, 3], p[0, 2], p[1, 3], p[0, 0], p[3, 3], p[3, 2], p[1, 0], p[3, 0]);
        p[2, 1] = _Coons(p[3, 0], p[3, 1], p[2, 0], p[3, 3], p[0, 0], p[0, 1], p[2, 3], p[0, 3]);
        p[2, 2] = _Coons(p[3, 3], p[3, 2], p[2, 3], p[3, 0], p[0, 3], p[0, 2], p[2, 0], p[0, 0]);
      }

      // Subdivide finely enough for the curvature; colour is bilinear in (u, v) from the corners.
      const int steps = 16;
      var start = this._vertices.Count;
      for (var j = 0; j <= steps; ++j)
        for (var i = 0; i <= steps; ++i) {
          var u = (double)i / steps;
          var v = (double)j / steps;
          var (x, y) = _Evaluate(p, u, v);
          var c = new double[this._components];
          for (var k = 0; k < this._components; ++k)
            c[k] = (1 - u) * (1 - v) * corner[0][k] + (1 - u) * v * corner[1][k] + u * v * corner[2][k] + u * (1 - v) * corner[3][k];
          this._vertices.Add((x, y, c));
        }

      for (var j = 0; j < steps; ++j)
        for (var i = 0; i < steps; ++i) {
          var a = start + j * (steps + 1) + i;
          this._triangles.Add((a, a + 1, a + steps + 1));
          this._triangles.Add((a + 1, a + steps + 2, a + steps + 1));
        }
    }

    // P11 = (−4·P00 + 6·(P01 + P10) − 2·(P03 + P30) + 3·(P31 + P13) − P33) / 9, and its rotations.
    private static (double X, double Y) _Coons((double X, double Y) p00, (double X, double Y) p01, (double X, double Y) p10, (double X, double Y) p03, (double X, double Y) p30, (double X, double Y) p31, (double X, double Y) p13, (double X, double Y) p33)
      => ((-4 * p00.X + 6 * (p01.X + p10.X) - 2 * (p03.X + p30.X) + 3 * (p31.X + p13.X) - p33.X) / 9,
          (-4 * p00.Y + 6 * (p01.Y + p10.Y) - 2 * (p03.Y + p30.Y) + 3 * (p31.Y + p13.Y) - p33.Y) / 9);

    private static (double X, double Y) _Evaluate((double X, double Y)[,] p, double u, double v) {
      Span<double> bu = [(1 - u) * (1 - u) * (1 - u), 3 * u * (1 - u) * (1 - u), 3 * u * u * (1 - u), u * u * u];
      Span<double> bv = [(1 - v) * (1 - v) * (1 - v), 3 * v * (1 - v) * (1 - v), 3 * v * v * (1 - v), v * v * v];
      double x = 0, y = 0;
      for (var i = 0; i < 4; ++i)
        for (var j = 0; j < 4; ++j) {
          var w = bu[i] * bv[j];
          x += w * p[i, j].X;
          y += w * p[i, j].Y;
        }

      return (x, y);
    }

    public override PixelBox? Extent(Matrix2D toDevice, int width, int height) {
      double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
      foreach (var v in this._vertices) {
        var (x, y) = toDevice.Apply(v.X, v.Y);
        minX = Math.Min(minX, x);
        minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x);
        maxY = Math.Max(maxY, y);
      }

      if (minX > maxX)
        return new PixelBox(0, 0, 0, 0);

      return new PixelBox((int)Math.Max(0, Math.Floor(minX)), (int)Math.Max(0, Math.Floor(minY)), (int)Math.Min(width, Math.Ceiling(maxX) + 1), (int)Math.Min(height, Math.Ceiling(maxY) + 1));
    }

    public override PdfPaint CreatePaint(Matrix2D toDevice, PixelBox area, bool useBackground) {
      var w = Math.Max(1, area.X1 - area.X0);
      var h = Math.Max(1, area.Y1 - area.Y0);
      var rgb = new float[w * h * 3];
      var covered = new bool[w * h];

      // Device positions of every vertex, and its colour as RGB when there is no function (the
      // components interpolate in the shading's own space only when there is a function: the
      // parameter is interpolated and then mapped).
      var dx = new double[this._vertices.Count];
      var dy = new double[this._vertices.Count];
      for (var i = 0; i < this._vertices.Count; ++i)
        (dx[i], dy[i]) = toDevice.Apply(this._vertices[i].X, this._vertices[i].Y);

      var n = this._components;
      Span<double> values = stackalloc double[n];
      Span<double> color = stackalloc double[3];
      var cache = new Dictionary<long, (float, float, float)>();
      foreach (var (a, b, c) in this._triangles) {
        double ax = dx[a] - area.X0, ay = dy[a] - area.Y0, bx = dx[b] - area.X0, by = dy[b] - area.Y0, cx = dx[c] - area.X0, cy = dy[c] - area.Y0;
        var denominator = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
        if (Math.Abs(denominator) < 1e-12)
          continue;

        var y0 = Math.Max(0, (int)Math.Ceiling(Math.Min(ay, Math.Min(by, cy)) - 0.5));
        var y1 = Math.Min(h - 1, (int)Math.Floor(Math.Max(ay, Math.Max(by, cy)) - 0.5));
        var x0 = Math.Max(0, (int)Math.Ceiling(Math.Min(ax, Math.Min(bx, cx)) - 0.5));
        var x1 = Math.Min(w - 1, (int)Math.Floor(Math.Max(ax, Math.Max(bx, cx)) - 0.5));
        var ca = this._vertices[a].C;
        var cb = this._vertices[b].C;
        var cc = this._vertices[c].C;
        for (var y = y0; y <= y1; ++y)
          for (var x = x0; x <= x1; ++x) {
            var px = x + 0.5;
            var py = y + 0.5;
            var l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / denominator;
            var l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / denominator;
            var l3 = 1 - l1 - l2;
            const double tolerance = -1e-9;
            if (l1 < tolerance || l2 < tolerance || l3 < tolerance)
              continue;

            for (var k = 0; k < n; ++k)
              values[k] = l1 * ca[k] + l2 * cb[k] + l3 * cc[k];

            long key = 0;
            for (var k = 0; k < n && k < 4; ++k)
              key = key * 4099 + (long)Math.Round(Math.Clamp(values[k], -8, 8) * 256 + 2048);
            if (!cache.TryGetValue(key, out var hit)) {
              this.ToRgb(values, color);
              hit = ((float)Math.Clamp(color[0], 0, 1), (float)Math.Clamp(color[1], 0, 1), (float)Math.Clamp(color[2], 0, 1));
              if (cache.Count < 1 << 18)
                cache[key] = hit;
            }

            var at = y * w + x;
            (rgb[at * 3], rgb[at * 3 + 1], rgb[at * 3 + 2]) = hit;
            covered[at] = true;
          }
      }

      return new RasterPaint(area.X0, area.Y0, w, h, rgb, covered, useBackground ? this.BackgroundRgb() : null);
    }

    private sealed class RasterPaint(int left, int top, int width, int height, float[] rgb, bool[] covered, (float R, float G, float B)? background) : PdfPaint {
      public override void Fill(int y, int x0, int count, Span<float> output, Span<float> alpha) {
        var row = y - top;
        for (var i = 0; i < count; ++i) {
          var col = x0 + i - left;
          if ((uint)row < (uint)height && (uint)col < (uint)width && covered[row * width + col]) {
            var at = (row * width + col) * 3;
            output[i * 3] = rgb[at];
            output[i * 3 + 1] = rgb[at + 1];
            output[i * 3 + 2] = rgb[at + 2];
          } else if (background is { } bg) {
            output[i * 3] = bg.R;
            output[i * 3 + 1] = bg.G;
            output[i * 3 + 2] = bg.B;
          } else
            alpha[i] = 0;
        }
      }
    }
  }

  /// <summary>Reads big-endian bit fields, as mesh streams pack their vertices.</summary>
  private sealed class BitReader(byte[] data) {
    private long _bit;

    public bool HasBits(int count) => this._bit + count <= (long)data.Length * 8;

    public ulong Read(int count) {
      ulong value = 0;
      for (var i = 0; i < count; ++i, ++this._bit) {
        var at = this._bit >> 3;
        var bit = at < data.Length ? (data[at] >> (7 - (int)(this._bit & 7))) & 1 : 0;
        value = (value << 1) | (uint)bit;
      }

      return value;
    }

    public void AlignToByte() => this._bit = (this._bit + 7) & ~7L;
  }
}
