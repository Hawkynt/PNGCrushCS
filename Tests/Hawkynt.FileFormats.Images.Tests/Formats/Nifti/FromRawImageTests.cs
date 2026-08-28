using System;
using FileFormat.Core;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Rgb24(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 11);
      data[i * 3 + 2] = (byte)(i * 23);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static RawImage _Gray16(int width, int height) {
    var data = new byte[width * height * 2];
    for (var i = 0; i < width * height; ++i) {
      data[i * 2] = (byte)(i * 3);
      data[i * 2 + 1] = (byte)(250 - i);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = data };
  }

  private static NiftiFile _Reread(NiftiFile file) => NiftiReader.FromBytes(NiftiWriter.ToBytes(file));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Colour_ReproducesExactly() {
    var source = _Rgb24(17, 6);
    var decoded = NiftiFile.ToRawImage(_Reread(NiftiFile.FromRawImage(source)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((17, 6)));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ColourWithAlpha_UsesStandardRgba32AndReproducesExactly() {
    var data = new byte[11 * 4 * 4];
    for (var i = 0; i < data.Length / 4; ++i) {
      data[i * 4] = (byte)(i * 7);
      data[i * 4 + 1] = (byte)(i * 11);
      data[i * 4 + 2] = (byte)(i * 23);
      data[i * 4 + 3] = (byte)(255 - i * 5);
    }

    var source = new RawImage { Width = 11, Height = 4, Format = PixelFormat.Rgba32, PixelData = data };
    var file = _Reread(NiftiFile.FromRawImage(source));
    var decoded = NiftiFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Datatype, Is.EqualTo(NiftiDataType.Rgba32));
      Assert.That(file.Bitpix, Is.EqualTo(32));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenBitGrey_ReproducesExactlyAtFullDepth() {
    // A scan is measurement: sixteen-bit voxels stay sixteen-bit rather than being crushed to eight.
    var source = _Gray16(9, 5);
    var file = _Reread(NiftiFile.FromRawImage(source));
    var decoded = NiftiFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Datatype, Is.EqualTo(NiftiDataType.UInt16));
      Assert.That(file.Bitpix, Is.EqualTo(16));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray16));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EightBitGrey_ReproducesExactly() {
    var data = new byte[8 * 4];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7);

    var source = new RawImage { Width = 8, Height = 4, Format = PixelFormat.Gray8, PixelData = data };
    var file = _Reread(NiftiFile.FromRawImage(source));
    var decoded = PixelConverter.Convert(NiftiFile.ToRawImage(file), PixelFormat.Gray8);

    Assert.Multiple(() => {
      Assert.That(file.Datatype, Is.EqualTo(NiftiDataType.UInt8));
      Assert.That(decoded.PixelData, Is.EqualTo(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var file = NiftiFile.FromRawImage(_Rgb24(64, 3));

    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height), Is.EqualTo((64, 3)));
      Assert.That(file.Depth, Is.EqualTo(1));
    });
  }
}
