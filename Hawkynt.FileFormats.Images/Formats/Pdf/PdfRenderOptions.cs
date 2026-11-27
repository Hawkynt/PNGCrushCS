using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Pdf;

/// <summary>How a PDF page is rendered.</summary>
public sealed class PdfRenderOptions {

  /// <summary>The default cap on a rendered page, in pixels: 16 megapixels, a 4096×4096 square.</summary>
  public const long DefaultMaximumPixels = 16L * 1024 * 1024;

  /// <summary>The default options: 72 dots per inch, one pixel per point, annotations shown.</summary>
  public static PdfRenderOptions Default { get; } = new();

  /// <summary>Pixels per inch. At the default 72 a page is as many pixels as it is points.</summary>
  public double Resolution { get; init; } = 72;

  /// <summary>
  /// The most pixels a page may come out as. A page whose size at <see cref="Resolution"/> would
  /// exceed it is rendered at the largest resolution that fits instead, keeping its proportions.
  /// </summary>
  public long MaximumPixels { get; init; } = DefaultMaximumPixels;

  /// <summary>Whether annotations' normal appearances are drawn over the page, as viewers do.</summary>
  public bool RenderAnnotations { get; init; } = true;

  internal void Validate() {
    if (!(this.Resolution > 0) || !double.IsFinite(this.Resolution))
      throw new ArgumentOutOfRangeException(nameof(this.Resolution), "The resolution must be a positive number.");
    if (this.MaximumPixels < 1)
      throw new ArgumentOutOfRangeException(nameof(this.MaximumPixels), "The pixel cap must be at least one.");
  }
}

/// <summary>A rendered page, and what could not be drawn on it.</summary>
/// <param name="Image">The page, as opaque RGB over a white background.</param>
/// <param name="Omissions">
/// Things the page asks for that were left out rather than drawn wrongly — text in a font that is
/// neither embedded nor substitutable, for instance. Empty when the page is complete.
/// </param>
public sealed record PdfPageRendering(RawImage Image, IReadOnlyList<string> Omissions);
