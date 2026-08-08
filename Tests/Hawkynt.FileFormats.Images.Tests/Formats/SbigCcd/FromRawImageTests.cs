using System;
using FileFormat.Core;

namespace FileFormat.SbigCcd.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_EightBitGrey_ReproducesExactly() {
    var data = new byte[21 * 7];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 5);

    var source = new RawImage { Width = 21, Height = 7, Format = PixelFormat.Gray8, PixelData = data };
    var decoded = SbigCcdFile.ToRawImage(
      SbigCcdReader.FromBytes(SbigCcdWriter.ToBytes(SbigCcdFile.FromRawImage(source))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((21, 7)));
    for (var i = 0; i < data.Length; ++i)
      Assert.That(decoded.PixelData[i * 3], Is.EqualTo(data[i]), $"well {i}");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenBitGrey_KeepsTheLowByteTheDecoderNeverShows() {
    // A CCD well count is stretched later, so the eight bits below the ones drawn are the point.
    var data = new byte[8 * 4 * 2];
    for (var i = 0; i < 32; ++i) {
      data[i * 2] = (byte)(i * 3);
      data[i * 2 + 1] = (byte)(190 - i);
    }

    var source = new RawImage { Width = 8, Height = 4, Format = PixelFormat.Gray16, PixelData = data };
    var restored = SbigCcdReader.FromBytes(SbigCcdWriter.ToBytes(SbigCcdFile.FromRawImage(source)));

    for (var i = 0; i < 32; ++i) {
      var stored = restored.PixelData[i * 2] | (restored.PixelData[i * 2 + 1] << 8);
      var expected = (data[i * 2] << 8) | data[i * 2 + 1];
      Assert.That(stored, Is.EqualTo(expected), $"well {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var file = SbigCcdFile.FromRawImage(new() {
      Width = 765, Height = 510, Format = PixelFormat.Gray8, PixelData = new byte[765 * 510]
    });

    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height), Is.EqualTo((765, 510)));
      Assert.That(file.PixelData, Has.Length.EqualTo(765 * 510 * 2));
    });
  }
}
