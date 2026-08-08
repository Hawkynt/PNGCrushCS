using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.OcpArtStudioWindow.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Even, so the pairs line up, and not a multiple of eight, so the stride is tested.</summary>
  private const int _WIDTH = 38;

  private const int _HEIGHT = 7;

  /// <summary>
  /// Hardware colours, each held for two columns — a mode 0 pixel covers two of the positions the
  /// picture is shown at, so only a picture already doubled can come back unchanged.
  /// </summary>
  private static RawImage _Doubled(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = ((x >> 1) + y * 3) % 16 * 3;
      var at = (y * width + x) * 3;
      data[at] = AmstradGraphics.Palette[color];
      data[at + 1] = AmstradGraphics.Palette[color + 1];
      data[at + 2] = AmstradGraphics.Palette[color + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThroughTheCompanion_ReproducesExactly() {
    var source = _Doubled(_WIDTH, _HEIGHT);
    var file = OcpArtStudioWindowFile.FromRawImage(source);

    var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
    try {
      var target = new FileInfo(Path.Combine(directory.FullName, "window.win"));
      File.WriteAllBytes(target.FullName, OcpArtStudioWindowWriter.ToBytes(file));
      File.WriteAllBytes(
        Path.ChangeExtension(target.FullName, OcpArtStudioWindowFile.CompanionExtension),
        OcpArtStudioWindowFile.PaletteFile(file));

      var decoded = OcpArtStudioWindowFile.ToRawImage(OcpArtStudioWindowReader.FromFile(target));

      Assert.Multiple(() => {
        Assert.That((decoded.Width, decoded.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
        Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
      });
    } finally {
      directory.Delete(true);
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureLargerThanAScreen() {
    var file = OcpArtStudioWindowFile.FromRawImage(_Doubled(640, 400));

    Assert.That(
      (file.Width, file.Height),
      Is.EqualTo((OcpArtStudioWindowFile.MaximumWidth, OcpArtStudioWindowFile.MaximumHeight)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CountsScreenPositionsRatherThanPixels() {
    // The stored width is twice the width the picture is shown at, and it sits at the end of the
    // file rather than the start — halving it once instead of twice is the mistake it invites.
    var bytes = OcpArtStudioWindowWriter.ToBytes(OcpArtStudioWindowFile.FromRawImage(_Doubled(_WIDTH, _HEIGHT)));

    Assert.That(bytes[^4] | (bytes[^3] << 8), Is.EqualTo(_WIDTH * 2));
  }
}
