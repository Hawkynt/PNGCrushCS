using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.PostScript;

/// <summary>Which colour model the program last named, so a colour can be turned into ink.</summary>
public enum PsColourSpace {

  /// <summary>One component, nought black and one white.</summary>
  Gray,

  /// <summary>Three components.</summary>
  Rgb,

  /// <summary>Four components, subtractive.</summary>
  Cmyk
}

/// <summary>
/// Everything <c>gsave</c> puts away and <c>grestore</c> brings back.
/// </summary>
/// <remarks>
/// The set is the one the reference lists for the graphics state, minus the parts that only matter
/// to a device with a screen and a transfer function: the current transformation matrix, the colour,
/// the line parameters, the dash, the clipping path and the current path. The current path is in
/// there because PostScript puts it there — a <c>gsave</c> in the middle of building a path and a
/// <c>grestore</c> after it leaves the half-built path exactly as it was, and a program that relies
/// on that is relying on something stated.
/// <para/>
/// The path is held in device coordinates, which is also what the language says: a coordinate is
/// transformed by the matrix in force when the segment is added, so a <c>translate</c> half way
/// through a path moves only what comes after it.
/// </remarks>
public sealed class PsGraphicsState {

  /// <summary>The current transformation matrix, from user space to pixels.</summary>
  public Matrix2D Ctm = Matrix2D.Identity;

  /// <summary>Which colour model the components are in.</summary>
  public PsColourSpace Space = PsColourSpace.Gray;

  /// <summary>The colour components, as many as the space has.</summary>
  public double[] Components = [0];

  /// <summary>The colour the components come to.</summary>
  public Rgba32 Colour = Rgba32.Black;

  /// <summary>The line width, in user space units.</summary>
  public double LineWidth = 1;

  /// <summary>What the ends of an open stroked path look like.</summary>
  public LineCap Cap = LineCap.Butt;

  /// <summary>What the corners of a stroked path look like.</summary>
  public LineJoin Join = LineJoin.Miter;

  /// <summary>How far a mitre may run before it is cut off.</summary>
  public double MiterLimit = 10;

  /// <summary>The on and off lengths of the dash, in user space units, or nothing for a solid line.</summary>
  public double[] Dash = [];

  /// <summary>How far into the dash pattern a path starts.</summary>
  public double DashOffset;

  /// <summary>What drawing is confined to, or nothing for the whole page.</summary>
  public VectorMask? Clip;

  /// <summary>The path being built, in device coordinates.</summary>
  public VectorPath Path = new();

  /// <summary>Where the path is, in device coordinates, or nothing when there is no current point.</summary>
  public (double X, double Y)? Current;

  /// <summary>Where the subpath being built began, for <c>closepath</c>.</summary>
  public (double X, double Y)? SubPathStart;

  /// <summary>Whether the path has anything in it.</summary>
  public bool HasPath;

  /// <summary>The font the program last selected, which this carries and does not draw with.</summary>
  public PsObject Font = PsObject.Null;

  /// <summary>
  /// Whether painting goes nowhere, which is what a null device is.
  /// </summary>
  /// <remarks>
  /// <c>nulldevice</c> installs a device that marks nothing, and programs use it to work something
  /// out — how wide a string is, whether a colour is in a plate — without disturbing the page. It is
  /// part of the graphics state, so a <c>grestore</c> brings the real page back, which is how every
  /// program that uses it gets out again.
  /// </remarks>
  public bool Discards;

  /// <summary>A copy that shares nothing writable with this one.</summary>
  public PsGraphicsState Clone() {
    var copy = (PsGraphicsState)this.MemberwiseClone();
    copy.Components = (double[])this.Components.Clone();
    copy.Dash = (double[])this.Dash.Clone();
    copy.Path = _CopyPath(this.Path);
    return copy;
  }

  private static VectorPath _CopyPath(VectorPath path) {
    var copy = new VectorPath();
    foreach (var (xs, ys, closed) in path.SubPaths) {
      var x = xs.Span;
      var y = ys.Span;
      copy.MoveTo(x[0], y[0]);
      for (var i = 1; i < x.Length; ++i)
        copy.LineTo(x[i], y[i]);

      if (closed)
        copy.Close();
    }

    return copy;
  }
}

/// <summary>The page a PostScript program draws on, and the operations it draws with.</summary>
/// <remarks>
/// Everything the language can put on paper comes down to filling a path, stroking one, confining
/// later drawing to one, or laying down a raster. The rasteriser in <c>FileFormat.Core.Vector</c>
/// does all four, so this is the layer that turns the language's idea of each into a call on it and
/// nothing more.
/// </remarks>
public sealed class PsPage {

  /// <summary>The surface being drawn on.</summary>
  public VectorCanvas Canvas { get; }

  /// <summary>The transform from default user space onto the surface, which <c>initmatrix</c> restores.</summary>
  public Matrix2D DefaultMatrix { get; }

  /// <summary>Whether anything has been painted yet.</summary>
  public bool HasInk { get; private set; }

  /// <summary>Builds a page of the given size with the given default transform.</summary>
  public PsPage(VectorCanvas canvas, Matrix2D defaultMatrix) {
    this.Canvas = canvas;
    this.DefaultMatrix = defaultMatrix;
  }

  /// <summary>Paints inside the path.</summary>
  public void Fill(PsGraphicsState state, FillRule rule) {
    if (state.HasPath && !state.Discards)
      this._Fill(state.Path, rule, VectorPaint.Solid(state.Colour), state.Clip);
  }

  /// <summary>Paints inside the path with a paint that varies across it.</summary>
  public void Fill(PsGraphicsState state, VectorPath path, FillRule rule, VectorPaint paint) {
    if (!state.Discards)
      this._Fill(path, rule, paint, state.Clip);
  }

  private void _Fill(VectorPath path, FillRule rule, VectorPaint paint, VectorMask? clip) {
    this.Canvas.Fill(path, rule, paint, null, clip);
    this.HasInk = true;
  }

  /// <summary>Draws a line along the path, dashed as the state says.</summary>
  public void Stroke(PsGraphicsState state) {
    if (!state.HasPath || state.Discards)
      return;

    var scale = Math.Max(state.Ctm.MeanScale, 1e-12);
    var width = state.LineWidth * scale;
    var path = state.Path;

    if (state.Dash.Length > 0) {
      var pattern = new double[state.Dash.Length];
      var total = 0.0;
      for (var i = 0; i < pattern.Length; ++i) {
        pattern[i] = state.Dash[i] * scale;
        total += pattern[i];
      }

      // A pattern whose lengths come to nothing once scaled would cut the path into nothing at all;
      // the reference says such a pattern draws a solid line, so it does.
      if (total > 1e-9)
        path = path.Dashed(pattern, state.DashOffset * scale);
    }

    this.Canvas.Stroke(path, width, VectorPaint.Solid(state.Colour), state.Clip, state.Join, state.Cap, state.MiterLimit);
    this.HasInk = true;
  }

  /// <summary>Confines later drawing to the part of the current clip the path also covers.</summary>
  public void Clip(PsGraphicsState state, FillRule rule) {
    // An empty path clips everything away, which is what filling nothing would mean, and a program
    // that does it means it.
    var mask = this.Canvas.MaskOf(state.HasPath ? state.Path : new(), rule);
    state.Clip = state.Clip == null ? mask : state.Clip.IntersectedWith(mask);
  }
}

/// <summary>Turning a colour in one of the three models the language has into ink.</summary>
public static class PsColour {

  /// <summary>A grey, nought black and one white.</summary>
  public static Rgba32 FromGray(double gray) {
    var v = _Byte(gray);
    return new(v, v, v);
  }

  /// <summary>A colour in red, green and blue.</summary>
  public static Rgba32 FromRgb(double red, double green, double blue) => new(_Byte(red), _Byte(green), _Byte(blue));

  /// <summary>
  /// A colour in cyan, magenta, yellow and black.
  /// </summary>
  /// <remarks>
  /// The conversion in the reference: each additive component is one minus the sum of its
  /// subtractive one and the black, clamped. It is the same arithmetic every renderer without a
  /// colour-managed workflow uses, and it is what the file's own numbers say without a profile to
  /// interpret them by.
  /// </remarks>
  public static Rgba32 FromCmyk(double cyan, double magenta, double yellow, double black) => new(
    _Byte(1 - Math.Min(1, cyan + black)),
    _Byte(1 - Math.Min(1, magenta + black)),
    _Byte(1 - Math.Min(1, yellow + black))
  );

  /// <summary>The colour a set of components in a space comes to.</summary>
  public static Rgba32 From(PsColourSpace space, IReadOnlyList<double> components) => space switch {
    PsColourSpace.Gray => FromGray(components.Count > 0 ? components[0] : 0),
    PsColourSpace.Rgb => FromRgb(_At(components, 0), _At(components, 1), _At(components, 2)),
    _ => FromCmyk(_At(components, 0), _At(components, 1), _At(components, 2), _At(components, 3))
  };

  private static double _At(IReadOnlyList<double> components, int index) => index < components.Count ? components[index] : 0;

  private static byte _Byte(double value) => (byte)Math.Clamp((int)(Math.Clamp(value, 0, 1) * 255 + 0.5), 0, 255);
}

/// <summary>An affine transform that can be undone, which <c>itransform</c> and clipping both need.</summary>
public static class PsMatrix {

  /// <summary>The transform that undoes this one.</summary>
  /// <exception cref="InvalidDataException">The transform flattens the plane and cannot be undone.</exception>
  public static Matrix2D Inverse(Matrix2D matrix) {
    var determinant = matrix.Determinant;
    if (Math.Abs(determinant) < 1e-15 || !double.IsFinite(determinant))
      throw new InvalidDataException($"A PostScript transform {matrix} maps the page onto a line and cannot be undone.");

    var a = matrix.D / determinant;
    var b = -matrix.B / determinant;
    var c = -matrix.C / determinant;
    var d = matrix.A / determinant;

    return new(a, b, c, d, -(matrix.E * a + matrix.F * c), -(matrix.E * b + matrix.F * d));
  }
}
