using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffDctv;

namespace FileFormat.IffDctv.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SamplesAndGeometryPreserved() {
    var original = IffDctvFile.FromRawImage(_Ramp(320, 32));

    var bytes = IffDctvWriter.ToBytes(original);
    var restored = IffDctvReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.ContentHeight, Is.EqualTo(original.ContentHeight));
      Assert.That(restored.Interlaced, Is.EqualTo(original.Interlaced));
      Assert.That(restored.PlaneCount, Is.EqualTo(original.PlaneCount));
      Assert.That(restored.Samples, Is.EqualTo(original.Samples));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = IffDctvFile.FromRawImage(_Ramp(320, 16));
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dctv");
    try {
      IffDctvWriter.ToFile(original, new FileInfo(path));
      var restored = IffDctvReader.FromFile(new FileInfo(path));

      Assert.That(restored.Samples, Is.EqualTo(original.Samples));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  /// <remarks>
  /// A grey ramp is the one thing DCTV carries almost exactly: with no colour to modulate, the
  /// samples are pure luminance and the only loss is the two-sample average the decoder takes. The
  /// first few and the last two columns are excluded because the reconstruction has no earlier
  /// sample to average against at the start of a line and no partner at its end, which is the
  /// format's own edge behaviour and shows up in genuine DCTV files too.
  /// </remarks>
  [Test]
  [Category("Integration")]
  public void GreyRamp_SurvivesWithinTheAveragingTheFormatImposes() {
    const int width = 320;
    const int height = 32;
    var source = _Grey(width, height);

    var decoded = IffDctvFile.ToRawImage(IffDctvReader.FromBytes(
      IffDctvWriter.ToBytes(IffDctvFile.FromRawImage(source))));

    Assert.That(decoded.Width, Is.EqualTo(width));
    Assert.That(decoded.Height, Is.EqualTo(height));

    var worst = 0;
    for (var y = 0; y < height; ++y)
      for (var x = 6; x < width - 2; ++x) {
        var offset = (y * width + x) * 3;
        for (var c = 0; c < 3; ++c)
          worst = Math.Max(worst, Math.Abs(source.PixelData[offset + c] - decoded.PixelData[offset + c]));
      }

    Assert.That(worst, Is.LessThanOrEqualTo(8), "a grey ramp should survive a DCTV round trip nearly intact");
  }

  /// <remarks>
  /// Colour does not survive intact and cannot: chrominance is carried at half the horizontal and
  /// half the vertical rate. This only pins down that a coloured picture comes back recognisably the
  /// same picture rather than noise.
  /// </remarks>
  [Test]
  [Category("Integration")]
  public void ColouredPicture_ComesBackAsTheSamePicture() {
    const int width = 320;
    const int height = 64;
    var source = _Ramp(width, height);

    var decoded = IffDctvFile.ToRawImage(IffDctvReader.FromBytes(
      IffDctvWriter.ToBytes(IffDctvFile.FromRawImage(source))));

    long total = 0;
    var counted = 0;
    for (var y = 4; y < height; ++y)
      for (var x = 8; x < width - 8; ++x) {
        var offset = (y * width + x) * 3;
        for (var c = 0; c < 3; ++c) {
          total += Math.Abs(source.PixelData[offset + c] - decoded.PixelData[offset + c]);
          ++counted;
        }
      }

    Assert.That(total / (double)counted, Is.LessThan(12.0));
  }

  private static RawImage _Ramp(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var offset = (y * width + x) * 3;
        pixels[offset] = (byte)(64 + x * 128 / (width - 1));
        pixels[offset + 1] = (byte)(96 + y * 96 / Math.Max(1, height - 1));
        pixels[offset + 2] = 128;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Grey(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var level = (byte)(x * 255 / (width - 1));
        var offset = (y * width + x) * 3;
        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = level;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
