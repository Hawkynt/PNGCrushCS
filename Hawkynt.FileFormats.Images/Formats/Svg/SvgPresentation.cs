using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.Svg;

/// <summary>How a shape is painted: everything inherited down the tree.</summary>
/// <remarks>
/// The properties that matter to geometry and colour, and nothing else. Each one is either set on
/// the element, set by a rule that matches it, or inherited from its parent — the specification's
/// own order, and the reason the state is copied down rather than looked up.
/// </remarks>
public sealed record SvgPresentation {

  /// <summary>What the inside of a shape is painted with, or null for nothing.</summary>
  public string? Fill { get; init; } = "black";

  /// <summary>What the outline is painted with, or null for nothing.</summary>
  public string? Stroke { get; init; }

  /// <summary>How wide the outline is, in user units.</summary>
  public double StrokeWidth { get; init; } = 1;

  /// <summary>Which points the fill counts as inside.</summary>
  public FillRule FillRule { get; init; } = FillRule.NonZero;

  /// <summary>What the outline does where two segments meet.</summary>
  public LineJoin LineJoin { get; init; } = LineJoin.Miter;

  /// <summary>What the outline does at the ends of an open path.</summary>
  public LineCap LineCap { get; init; } = LineCap.Butt;

  /// <summary>How far the miter may run before it is cut off.</summary>
  public double MiterLimit { get; init; } = 4;

  /// <summary>The on and off runs of a dashed outline, in user units.</summary>
  public double[] Dashes { get; init; } = [];

  /// <summary>How far into the dash pattern the outline starts.</summary>
  public double DashOffset { get; init; }

  /// <summary>How opaque the whole element is.</summary>
  public double Opacity { get; init; } = 1;

  /// <summary>How opaque the fill is, on top of the element's own opacity.</summary>
  public double FillOpacity { get; init; } = 1;

  /// <summary>How opaque the outline is, on top of the element's own opacity.</summary>
  public double StrokeOpacity { get; init; } = 1;

  /// <summary>Whether the element and everything under it is drawn at all.</summary>
  public bool Visible { get; init; } = true;

  /// <summary>The colour <c>currentColor</c> stands for.</summary>
  public Rgba32 CurrentColour { get; init; } = Rgba32.Black;

  /// <summary>This state with a set of declarations laid over it.</summary>
  public SvgPresentation With(Dictionary<string, string> declarations) {
    if (declarations.Count == 0)
      return this;

    var result = this;
    foreach (var (property, raw) in declarations) {
      var value = raw.Trim();
      if (value.Length == 0 || value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
        continue;

      switch (property) {
        case "fill":
          result = result with { Fill = value };
          break;
        case "stroke":
          result = result with { Stroke = value };
          break;
        case "stroke-width":
          if (SvgLength.TryPixels(value, 0, out var width))
            result = result with { StrokeWidth = Math.Max(width, 0) };
          break;
        case "fill-rule":
        case "clip-rule":
          if (property == "fill-rule")
            result = result with { FillRule = value.Equals("evenodd", StringComparison.OrdinalIgnoreCase) ? FillRule.EvenOdd : FillRule.NonZero };
          break;
        case "stroke-linejoin":
          result = result with {
            LineJoin = value.ToLowerInvariant() switch {
              "round" => LineJoin.Round,
              "bevel" => LineJoin.Bevel,
              _ => LineJoin.Miter
            }
          };
          break;
        case "stroke-linecap":
          result = result with {
            LineCap = value.ToLowerInvariant() switch {
              "round" => LineCap.Round,
              "square" => LineCap.Square,
              _ => LineCap.Butt
            }
          };
          break;
        case "stroke-miterlimit":
          if (SvgLength.TryNumber(value, out var limit) && limit >= 1)
            result = result with { MiterLimit = limit };
          break;
        case "stroke-dasharray":
          result = result with { Dashes = value.Equals("none", StringComparison.OrdinalIgnoreCase) ? [] : SvgLength.Numbers(value) };
          break;
        case "stroke-dashoffset":
          if (SvgLength.TryPixels(value, 0, out var offset))
            result = result with { DashOffset = offset };
          break;
        case "opacity":
          result = result with { Opacity = _Alpha(value) };
          break;
        case "fill-opacity":
          result = result with { FillOpacity = _Alpha(value) };
          break;
        case "stroke-opacity":
          result = result with { StrokeOpacity = _Alpha(value) };
          break;
        case "display":
          result = result with { Visible = !value.Equals("none", StringComparison.OrdinalIgnoreCase) };
          break;
        case "visibility":
          result = result with { Visible = !value.Equals("hidden", StringComparison.OrdinalIgnoreCase) && !value.Equals("collapse", StringComparison.OrdinalIgnoreCase) };
          break;
        case "color":
          if (SvgColour.TryParse(value, out var current))
            result = result with { CurrentColour = current };
          break;
      }
    }

    return result;
  }

  /// <summary>The properties that can be written as attributes as well as in a style.</summary>
  public static readonly string[] Attributes = [
    "fill", "stroke", "stroke-width", "fill-rule", "clip-rule", "stroke-linejoin", "stroke-linecap",
    "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "opacity", "fill-opacity",
    "stroke-opacity", "display", "visibility", "color"
  ];

  /// <summary>Splits a <c>style</c> attribute or a rule body into its declarations.</summary>
  public static Dictionary<string, string> ParseDeclarations(string? text) {
    var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(text))
      return declarations;

    foreach (var part in text.Split(';')) {
      var colon = part.IndexOf(':');
      if (colon <= 0)
        continue;

      var property = part[..colon].Trim().ToLowerInvariant();
      var value = part[(colon + 1)..].Trim();
      if (property.Length > 0 && value.Length > 0)
        declarations[property] = value;
    }

    return declarations;
  }

  private static double _Alpha(string value) {
    if (value.EndsWith('%') && SvgLength.TryNumber(value, out var percent))
      return Math.Clamp(percent / 100, 0, 1);

    return SvgLength.TryNumber(value, out var number) ? Math.Clamp(number, 0, 1) : 1;
  }
}
