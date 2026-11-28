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

  /// <summary>
  /// Whether overprinting (the OP, op and OPM graphics state entries) is simulated, as an overprint
  /// preview shows it: the page is drawn on CMYK and spot separations and each pixel's colour taken
  /// from them, so a colour that overprints leaves the inks it does not use. Off by default, as in
  /// viewers and poppler's pdftoppm without <c>-overprint</c>, which ignore overprinting on screen.
  /// With it on, every colour goes through CMYK, as it does in pdftoppm with <c>-overprint</c>.
  /// </summary>
  public bool SimulateOverprint { get; init; }

  /// <summary>
  /// Whether a CJK CID font the document does not embed is drawn with a font installed on this
  /// machine that covers its character collection (Adobe-Japan1, -GB1, -CNS1 or -Korea1), as viewers
  /// and poppler do; on by default. The platform's font folders are searched — the Windows, macOS or
  /// XDG and fontconfig ones — after any in <see cref="FontDirectories"/>. Where no installed font
  /// covers the collection the text is left out and reported in <see cref="PdfPageRendering.Omissions"/>.
  /// Turned off, only <see cref="FontDirectories"/> is searched, so a render no longer depends on
  /// what the machine has installed.
  /// </summary>
  public bool UseSystemFonts { get; init; } = true;

  /// <summary>
  /// Folders of TrueType and OpenType fonts and collections (.ttf, .otf, .ttc, .otc), searched with
  /// their subfolders before the system's for a font to draw a non-embedded CJK CID font with.
  /// Empty by default. Each set of folders is read once per process, the first time a page needs it.
  /// </summary>
  public IReadOnlyList<string> FontDirectories { get; init; } = [];

  internal void Validate() {
    if (!(this.Resolution > 0) || !double.IsFinite(this.Resolution))
      throw new ArgumentOutOfRangeException(nameof(this.Resolution), "The resolution must be a positive number.");
    if (this.MaximumPixels < 1)
      throw new ArgumentOutOfRangeException(nameof(this.MaximumPixels), "The pixel cap must be at least one.");
    ArgumentNullException.ThrowIfNull(this.FontDirectories, nameof(this.FontDirectories));
  }
}

/// <summary>A rendered page, and what could not be drawn on it.</summary>
/// <param name="Image">The page, as opaque RGB over a white background.</param>
/// <param name="Omissions">
/// Things the page asks for that were left out rather than drawn wrongly — text in a font that is
/// neither embedded nor substitutable, for instance. Empty when the page is complete.
/// </param>
public sealed record PdfPageRendering(RawImage Image, IReadOnlyList<string> Omissions);
