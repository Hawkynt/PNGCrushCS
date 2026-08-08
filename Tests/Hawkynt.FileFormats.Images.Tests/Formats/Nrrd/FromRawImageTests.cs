using System;
using FileFormat.Core;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static NrrdFile _Reread(NrrdFile file) => NrrdReader.FromBytes(NrrdWriter.ToBytes(file));

  private static byte[] _Ramp(int length, int step) {
    var data = new byte[length];
    for (var i = 0; i < length; ++i)
      data[i] = (byte)(i * step);

    return data;
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Colour_ReproducesExactly() {
    var source = new RawImage {
      Width = 13, Height = 7, Format = PixelFormat.Rgb24, PixelData = _Ramp(13 * 7 * 3, 5)
    };

    var file = _Reread(NrrdFile.FromRawImage(source));
    var decoded = NrrdFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Sizes, Is.EqualTo(new[] { 3, 13, 7 }));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenBitGrey_ReproducesExactlyAtFullDepth() {
    // NRRD says what it holds, so there is no reason to halve a sixteen-bit sample on the way in.
    var source = new RawImage {
      Width = 9, Height = 4, Format = PixelFormat.Gray16, PixelData = _Ramp(9 * 4 * 2, 3)
    };

    var file = _Reread(NrrdFile.FromRawImage(source));
    var decoded = NrrdFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.DataType, Is.EqualTo(NrrdType.UInt16));
      Assert.That(file.Sizes, Is.EqualTo(new[] { 9, 4 }));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray16));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenBitColour_ReproducesExactly() {
    var source = new RawImage {
      Width = 6, Height = 3, Format = PixelFormat.Rgb48, PixelData = _Ramp(6 * 3 * 6, 7)
    };

    var file = _Reread(NrrdFile.FromRawImage(source));
    var decoded = NrrdFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.DataType, Is.EqualTo(NrrdType.UInt16));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb48));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var file = NrrdFile.FromRawImage(new() {
      Width = 200, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[200 * 3 * 3]
    });

    Assert.Multiple(() => {
      Assert.That(file.Sizes, Is.EqualTo(new[] { 3, 200, 3 }));
      Assert.That(file.Encoding, Is.EqualTo(NrrdEncoding.Raw));
      Assert.That(file.Endian, Is.EqualTo("little"));
    });
  }
}
