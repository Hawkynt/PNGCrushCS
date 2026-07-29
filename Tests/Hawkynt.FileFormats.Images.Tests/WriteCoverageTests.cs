using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// <see cref="FormatRegistry.Write"/> takes an arbitrary <see cref="RawImage"/>, so a writer that
/// only accepts its own internal layout is unusable through the public API. These tests pin the
/// coverage so it can't silently regress.
/// </summary>
[TestFixture]
public sealed class WriteCoverageTests {

  /// <summary>Formats the CLI and README present as first-class write targets.</summary>
  private static readonly ImageFormat[] _HeadlineFormats = [
    ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif, ImageFormat.Tiff, ImageFormat.Bmp,
    ImageFormat.Tga, ImageFormat.Pcx, ImageFormat.WebP, ImageFormat.Qoi,
  ];

  /// <summary>Encoding must succeed from any source layout, not just one privileged one.</summary>
  private static readonly PixelFormat[] _SourceFormats = [
    PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Indexed8,
  ];

  private static RawImage _Sample(PixelFormat format, int width = 32, int height = 32) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o] = (byte)(x * 8);
      data[o + 1] = (byte)(y * 8);
      data[o + 2] = (byte)((x + y) % 2 == 0 ? 255 : 0);
      data[o + 3] = 255;
    }

    var bgra = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
    return format == PixelFormat.Bgra32 ? bgra : PixelConverter.Convert(bgra, format);
  }

  [Test]
  [Category("Unit")]
  public void Write_HeadlineFormats_AcceptEverySourcePixelFormat() {
    var failures = new List<string>();

    foreach (var format in _HeadlineFormats)
    foreach (var source in _SourceFormats) {
      try {
        var bytes = FormatRegistry.Write(_Sample(source), format);
        if (bytes is not { Length: > 0 })
          failures.Add($"{format} from {source}: produced no bytes");
      } catch (Exception ex) {
        failures.Add($"{format} from {source}: {ex.GetType().Name}: {ex.Message}");
      }
    }

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }

  [Test]
  [Category("Unit")]
  public void Write_HeadlineFormats_RoundTripToTheSameDimensions() {
    foreach (var format in _HeadlineFormats) {
      var source = _Sample(PixelFormat.Rgba32);
      var bytes = FormatRegistry.Write(source, format);
      Assert.That(bytes, Is.Not.Null.And.Not.Empty, $"{format}: encode failed");

      var back = FormatRegistry.Read(bytes!);
      Assert.That(back, Is.Not.Null, $"{format}: could not read back its own output");
      Assert.That((back!.Width, back.Height), Is.EqualTo((source.Width, source.Height)),
        $"{format}: dimensions changed across a round-trip");
    }
  }

  /// <summary>Common native resolutions for formats whose declared dimensions don't pin a usable size.</summary>
  private static readonly (int W, int H)[] _FallbackSizes = [
    (64, 64), (320, 200), (256, 192), (320, 192), (160, 200), (256, 240),
    (512, 342), (640, 400), (32, 32), (8, 8), (16, 16),
  ];

  /// <summary>
  /// A floor, not a target: writers were rejecting non-native input wholesale, so this guards the
  /// bulk of the write surface. Raise it as more formats gain full coverage.
  /// </summary>
  private const int _MINIMUM_ENCODABLE_FORMATS = 280;

  [Test]
  [Category("Unit")]
  public void Write_MostWritableFormats_AcceptAnArbitraryRgbaImage() {
    var writable = FormatRegistry.AllFormats.Where(e => e.SupportsWrite).ToList();
    var encodable = 0;

    foreach (var entry in writable) {
      // Formats pin their own dimensions, so try what each declares before falling back to a
      // sweep of common native resolutions.
      var sizes = new List<(int W, int H)>();
      foreach (var mode in entry.VideoModes ?? [])
      foreach (var (w, h) in mode.Dimensions)
        sizes.Add((w.SnapToValid(64), h.SnapToValid(64)));

      sizes.AddRange(_FallbackSizes);

      foreach (var (w, h) in sizes) {
        try {
          if (FormatRegistry.Write(_Sample(PixelFormat.Rgba32, w, h), entry.Format) is { Length: > 0 }) {
            ++encodable;
            break;
          }
        } catch (Exception) {
          // Try the next declared size before giving up on this format.
        }
      }
    }

    Assert.That(encodable, Is.GreaterThanOrEqualTo(_MINIMUM_ENCODABLE_FORMATS),
      $"only {encodable} of {writable.Count} writable formats accepted an arbitrary RGBA image");
  }

  [Test]
  [Category("Unit")]
  public void Write_ReadOnlyFormat_ReturnsNullRatherThanThrowing() {
    var readOnly = FormatRegistry.AllFormats.FirstOrDefault(e => !e.SupportsWrite);
    if (readOnly == null)
      Assert.Pass("every registered format supports writing");

    Assert.That(FormatRegistry.Write(_Sample(PixelFormat.Rgba32), readOnly!.Format), Is.Null);
  }
}
