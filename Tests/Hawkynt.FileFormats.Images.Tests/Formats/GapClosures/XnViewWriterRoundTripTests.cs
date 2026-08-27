using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// The six writers added for formats XnView settled the readers for.
/// </summary>
/// <remarks>
/// This is a regression guard, not a conformance result. No third-party converter is available in
/// this environment to read what these writers produce, and a writer agreeing only with our own
/// reader is not evidence the format was written correctly — the repository says so itself. What
/// this does catch is a writer that stops producing anything its own reader recognises, which is
/// the failure a silent refactor causes.
/// </remarks>
[TestFixture]
public sealed class XnViewWriterRoundTripTests {

  private static RawImage _Picture(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 17 % 256);
      rgb[at + 1] = (byte)(y * 29 % 256);
      rgb[at + 2] = (byte)((x + y) * 11 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  [TestCase(ImageFormat.SriSun, 8, 4)]
  [TestCase(ImageFormat.Ximage, 8, 4)]
  [TestCase(ImageFormat.Portrait, 512, 512)] // fixed size, and its writer says so by name
  [TestCase(ImageFormat.DigitalFx, 8, 4)]
  [TestCase(ImageFormat.MegaluxFrame, 8, 4)]
  [TestCase(ImageFormat.MicroDynamicsMars, 8, 4)]
  [Category("Unit")]
  public void WhatItWritesItsOwnReaderTakesBack(ImageFormat format, int width, int height) {
    var entry = FormatRegistry.AllFormats.Single(f => f.Format == format);
    Assert.That(entry.SupportsWrite, Is.True, $"{format} is expected to encode.");

    var source = _Picture(width, height);
    var bytes = FormatRegistry.Write(source, format);
    Assert.That(bytes, Is.Not.Null.And.Not.Empty, $"{format} wrote nothing.");

    var read = entry.LoadRawImageFromBytes(bytes!);
    Assert.That(read, Is.Not.Null, $"{format}'s own reader would not take back what its writer produced.");
    Assert.Multiple(() => {
      Assert.That(read!.Width, Is.EqualTo(source.Width));
      Assert.That(read.Height, Is.EqualTo(source.Height));
    });
  }
}
