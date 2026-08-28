using System;
using System.Linq;

namespace FileFormat.Core.Tests;

/// <summary>
/// Exercises every packed integer <see cref="PixelFormat"/> pair through <see cref="PixelConverter.Convert"/>.
/// The hub-based fallback used to recurse forever for targets with no direct route from BGRA32,
/// overflowing the stack — an uncatchable crash reachable from the public encode API.
/// </summary>
/// <remarks>
/// Planar YUV and floating-point formats deliberately live one layer above this converter in
/// <see cref="RawImageConverter"/>. Keeping them out of this matrix is not an exemption from conversion
/// coverage: <c>RawImageExtendedFormatTests</c> exercises those routes and their colour interpretation.
/// In particular, RGB-to-YUV cannot be a context-free byte shuffle: a writer has to choose a matrix,
/// signal range and chroma siting rather than have this low-level converter invent those semantics.
/// </remarks>
[TestFixture]
public sealed class PixelConverterMatrixTests {

  private static PixelFormat[] PackedIntegerFormats => Enum.GetValues<PixelFormat>()
    .Where(format => !RawImage.IsPlanarYuvFormat(format) && !RawImage.IsFloatingPointFormat(format))
    .ToArray();

  private static readonly PixelFormat[] _IndexedFormats = [
    PixelFormat.Indexed1, PixelFormat.Indexed4, PixelFormat.Indexed8, PixelFormat.Indexed16
  ];

  private static RawImage _MakeSource(PixelFormat format, int width = 8, int height = 8) {
    // Build in BGRA32 then convert, so each source is genuinely in its declared format.
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o] = (byte)(x * 32);
      data[o + 1] = (byte)(y * 32);
      data[o + 2] = (byte)((x + y) * 16);
      data[o + 3] = 255;
    }

    var bgra = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
    return format == PixelFormat.Bgra32 ? bgra : PixelConverter.Convert(bgra, format);
  }

  [Test]
  [Category("Unit")]
  public void Convert_EveryPackedIntegerFormatPair_Terminates() {
    foreach (var source in PackedIntegerFormats) {
      var image = _MakeSource(source);

      foreach (var target in PackedIntegerFormats) {
        var result = PixelConverter.Convert(image, target);

        Assert.That(result, Is.Not.Null, $"{source} -> {target} returned null");
        Assert.That(result.Format, Is.EqualTo(target), $"{source} -> {target} produced {result.Format}");
        Assert.That(result.Width, Is.EqualTo(image.Width), $"{source} -> {target} changed width");
        Assert.That(result.Height, Is.EqualTo(image.Height), $"{source} -> {target} changed height");
        Assert.That(result.PixelData, Is.Not.Null.And.Not.Empty, $"{source} -> {target} produced no pixels");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void Convert_ToIndexedFormat_AttachesPalette() {
    var source = _MakeSource(PixelFormat.Rgba32);

    foreach (var target in _IndexedFormats) {
      var result = PixelConverter.Convert(source, target);

      Assert.That(result.Palette, Is.Not.Null, $"{target} has no palette");
      Assert.That(result.PaletteCount, Is.GreaterThan(0), $"{target} has an empty palette");
      Assert.That(result.PaletteCount, Is.LessThanOrEqualTo(ColorQuantizer.MaxColorsFor(target)),
        $"{target} palette exceeds what its indices can address");
      Assert.That(result.Palette!.Length, Is.EqualTo(result.PaletteCount * 3));
    }
  }

  [Test]
  [Category("Unit")]
  public void Convert_ToIndexed_RoundTripsColoursThatFitThePalette() {
    // Four distinct colours fit every indexed format from Indexed4 up, so quantization must be exact.
    var data = new byte[4 * 4];
    ReadOnlySpan<byte> colours = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255];
    colours.CopyTo(data);

    var source = new RawImage { Width = 4, Height = 1, Format = PixelFormat.Bgra32, PixelData = data };

    foreach (var target in new[] { PixelFormat.Indexed4, PixelFormat.Indexed8, PixelFormat.Indexed16 }) {
      var indexed = PixelConverter.Convert(source, target);
      var back = PixelConverter.Convert(indexed, PixelFormat.Bgra32);

      Assert.That(back.PixelData, Is.EqualTo(data), $"{target} did not preserve a 4-colour image");
    }
  }

  [Test]
  [Category("Unit")]
  public void Convert_Indexed1_ProducesTwoColoursAtMost() {
    var source = _MakeSource(PixelFormat.Rgba32);
    var result = PixelConverter.Convert(source, PixelFormat.Indexed1);

    Assert.That(result.PaletteCount, Is.LessThanOrEqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo((source.Width * source.Height + 7) / 8));
  }

  [Test]
  [Category("Unit")]
  public void Convert_SamePackedIntegerFormat_ReturnsSourceUnchanged() {
    foreach (var format in PackedIntegerFormats) {
      var image = _MakeSource(format);
      Assert.That(PixelConverter.Convert(image, format), Is.SameAs(image), $"{format} was needlessly copied");
    }
  }
}
