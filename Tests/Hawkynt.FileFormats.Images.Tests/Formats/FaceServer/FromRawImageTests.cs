using System;
using FileFormat.Core;

namespace FileFormat.FaceServer.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A ramp whose first ten bytes hold no colon, so the reader's header sniff — which skips
  /// leading <c>key: value</c> lines — sees pixel data from the first byte and takes none of it.</summary>
  private static RawImage _GrayRamp() {
    var data = new byte[FaceServerFile.PixelCount];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)i;

    return new() {
      Width = FaceServerFile.FixedWidth,
      Height = FaceServerFile.FixedHeight,
      Format = PixelFormat.Gray8,
      PixelData = data,
    };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GrayRamp_ReproducesExactly() {
    var source = _GrayRamp();
    var file = FaceServerFile.FromRawImage(source);
    var restored = FaceServerReader.FromBytes(FaceServerWriter.ToBytes(file));
    var decoded = FaceServerFile.ToRawImage(restored);

    for (var i = 0; i < FaceServerFile.PixelCount; ++i)
      Assert.That(decoded.PixelData[i * 3], Is.EqualTo(source.PixelData[i]), $"pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    // The thumbnail is 48 by 48 and no other size, so a picture of another is brought to it.
    static RawImage Raw(int width, int height)
      => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = new byte[width * height * 3] };

    var small = FaceServerFile.FromRawImage(Raw(7, 9));
    var large = FaceServerFile.FromRawImage(Raw(640, 480));

    Assert.Multiple(() => {
      Assert.That(small.PixelData, Has.Length.EqualTo(FaceServerFile.PixelCount));
      Assert.That(large.PixelData, Has.Length.EqualTo(FaceServerFile.PixelCount));
    });
  }
}
