using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Svg;

/// <summary>Turns a gradient element into a paint the rasteriser can ask for a colour.</summary>
/// <remarks>
/// A gradient states its geometry either in the drawing's own coordinates or as a fraction of the
/// box the shape it paints falls in, which is the default and the reason a gradient has to be
/// resolved per shape rather than once. It may also point at another gradient for the parts it does
/// not state itself, including its stops, so the chain is followed.
/// </remarks>
public static class SvgGradient {

  /// <summary>How far a chain of gradients pointing at each other is followed.</summary>
  private const int _MaxChain = 8;

  /// <summary>The paint a <c>url(#id)</c> names, or null when it names something else.</summary>
  /// <param name="userToDevice">
  /// The transform in force where the gradient is referred to. A gradient stated in user space is
  /// stated in that space, so without it the ramp would sit wherever the drawing's own origin is
  /// rather than on the shape.
  /// </param>
  public static VectorPaint? Resolve(string reference, Dictionary<string, XElement> byId, VectorPath shape, Matrix2D userToDevice, double alpha) {
    var open = reference.IndexOf('#');
    var close = reference.LastIndexOf(')');
    if (open < 0)
      return null;

    var id = (close > open ? reference[(open + 1)..close] : reference[(open + 1)..]).Trim();
    if (!byId.TryGetValue(id, out var element))
      return null;

    var radial = element.Name.LocalName == "radialGradient";
    if (!radial && element.Name.LocalName != "linearGradient")
      return null;

    var chain = _Chain(element, byId);
    var stops = _Stops(chain, alpha);
    if (stops.Count == 0)
      return null;

    var spread = _Attribute(chain, "spreadMethod")?.ToLowerInvariant() switch {
      "reflect" => GradientSpread.Reflect,
      "repeat" => GradientSpread.Repeat,
      _ => GradientSpread.Pad
    };

    var onBoundingBox = !string.Equals(_Attribute(chain, "gradientUnits"), "userSpaceOnUse", StringComparison.Ordinal);
    var gradientTransform = SvgTransform.Parse(_Attribute(chain, "gradientTransform"));

    // The way from the gradient's own frame onto the surface: its own transform, and then either
    // the box the shape falls in or the user space the reference was made from. The paint is asked
    // for a colour at a pixel, so what it is given is the inverse of that.
    Matrix2D toDevice;
    if (onBoundingBox) {
      var bounds = shape.Bounds;
      if (bounds == null)
        return null;

      var (minX, minY, maxX, maxY) = bounds.Value;
      var width = maxX - minX;
      var height = maxY - minY;
      if (width <= 0 || height <= 0)
        return null;

      toDevice = Matrix2D.Scaling(width, height).Then(Matrix2D.Translation(minX, minY));
    } else
      toDevice = userToDevice;

    var toPaint = _Inverse(gradientTransform.Then(toDevice));

    if (radial) {
      var cx = _Number(chain, "cx", 0.5);
      var cy = _Number(chain, "cy", 0.5);
      var r = _Number(chain, "r", 0.5);
      return GradientPaint.Radial(stops, cx, cy, r, _Number(chain, "fx", cx), _Number(chain, "fy", cy), toPaint, spread);
    }

    return GradientPaint.Linear(stops, _Number(chain, "x1", 0), _Number(chain, "y1", 0), _Number(chain, "x2", 1), _Number(chain, "y2", 0), toPaint, spread);
  }

  /// <summary>The gradient and every gradient it inherits from, nearest first.</summary>
  private static List<XElement> _Chain(XElement element, Dictionary<string, XElement> byId) {
    var chain = new List<XElement> { element };
    var seen = new HashSet<XElement> { element };

    for (var i = 0; i < _MaxChain; ++i) {
      var href = chain[^1].Attribute("href")?.Value ?? chain[^1].Attribute("{http://www.w3.org/1999/xlink}href")?.Value;
      if (string.IsNullOrEmpty(href) || href[0] != '#' || !byId.TryGetValue(href[1..], out var next) || !seen.Add(next))
        break;

      chain.Add(next);
    }

    return chain;
  }

  private static string? _Attribute(List<XElement> chain, string name) {
    foreach (var element in chain) {
      var value = element.Attribute(name)?.Value;
      if (!string.IsNullOrEmpty(value))
        return value;
    }

    return null;
  }

  private static double _Number(List<XElement> chain, string name, double fallback) {
    var value = _Attribute(chain, name);
    if (value == null)
      return fallback;

    // A percentage here is a fraction of the gradient's own space, whichever space that is, so it
    // reduces to the number over a hundred without anything else being known.
    if (value.TrimEnd().EndsWith('%'))
      return SvgLength.TryNumber(value, out var percent) ? percent / 100 : fallback;

    return SvgLength.TryNumber(value, out var number) ? number : fallback;
  }

  private static List<GradientStop> _Stops(List<XElement> chain, double alpha) {
    foreach (var element in chain) {
      var stops = new List<GradientStop>();
      foreach (var child in element.Elements()) {
        if (child.Name.LocalName != "stop")
          continue;

        var declarations = SvgPresentation.ParseDeclarations(child.Attribute("style")?.Value);
        var colourText = declarations.GetValueOrDefault("stop-color") ?? child.Attribute("stop-color")?.Value;
        var opacityText = declarations.GetValueOrDefault("stop-opacity") ?? child.Attribute("stop-opacity")?.Value;
        var offsetText = child.Attribute("offset")?.Value;

        var offset = 0.0;
        if (offsetText != null && SvgLength.TryNumber(offsetText, out var parsed))
          offset = offsetText.TrimEnd().EndsWith('%') ? parsed / 100 : parsed;

        if (!SvgColour.TryParse(colourText, out var colour))
          colour = Rgba32.Black;

        var opacity = alpha;
        if (opacityText != null && SvgLength.TryNumber(opacityText, out var stopAlpha))
          opacity *= Math.Clamp(stopAlpha, 0, 1);

        stops.Add(new(Math.Clamp(offset, 0, 1), colour with { A = (byte)Math.Clamp((int)Math.Round(colour.A * opacity), 0, 255) }));
      }

      if (stops.Count > 0)
        return stops;
    }

    return [];
  }

  private static Matrix2D _Inverse(Matrix2D matrix) {
    var determinant = matrix.Determinant;
    if (Math.Abs(determinant) < 1e-12)
      return Matrix2D.Identity;

    var a = matrix.D / determinant;
    var b = -matrix.B / determinant;
    var c = -matrix.C / determinant;
    var d = matrix.A / determinant;

    return new(a, b, c, d, -(matrix.E * a + matrix.F * c), -(matrix.E * b + matrix.F * d));
  }
}
