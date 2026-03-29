using System;
using System.IO;
using FileFormat.Bpg;

namespace FileFormat.Bpg.Tests;

[TestFixture]
public sealed class BpgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BpgReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BpgReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bpg"));
    Assert.Throws<FileNotFoundException>(() => BpgReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BpgReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[20];
    bad[0] = 0x00;
    bad[1] = 0x00;
    bad[2] = 0x00;
    bad[3] = 0x00;
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var data = _BuildMinimalBpg(4, 3, BpgPixelFormat.Grayscale, 8, BpgColorSpace.Rgb);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.PixelFormat, Is.EqualTo(BpgPixelFormat.Grayscale));
    Assert.That(result.BitDepth, Is.EqualTo(8));
    Assert.That(result.ColorSpace, Is.EqualTo(BpgColorSpace.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesPixelFormat() {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.PixelFormat, Is.EqualTo(BpgPixelFormat.YCbCr444));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitDepth10_ParsesCorrectly() {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr420, 10, BpgColorSpace.YCbCrBT709);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.BitDepth, Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ColorSpaceBT709_ParsesCorrectly() {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr420, 8, BpgColorSpace.YCbCrBT709);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.ColorSpace, Is.EqualTo(BpgColorSpace.YCbCrBT709));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AlphaFlag_ParsesCorrectly() {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb, hasAlpha: true);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoAlpha_ParsesCorrectly() {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb, hasAlpha: false);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LargeDimensions_Ue7ParsesCorrectly() {
    var data = _BuildMinimalBpg(1920, 1080, BpgPixelFormat.YCbCr420, 8, BpgColorSpace.YCbCrBT709);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(1920));
    Assert.That(result.Height, Is.EqualTo(1080));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_ReadCorrectly() {
    var pixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.Grayscale, 8, BpgColorSpace.Rgb, pixelData: pixelData);
    var result = BpgReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = _BuildMinimalBpg(3, 2, BpgPixelFormat.Grayscale, 8, BpgColorSpace.Rgb);

    using var ms = new MemoryStream(data);
    var result = BpgReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  internal static byte[] _BuildMinimalBpg(
    int width,
    int height,
    BpgPixelFormat pixelFormat,
    int bitDepth,
    BpgColorSpace colorSpace,
    bool hasAlpha = false,
    byte[]? pixelData = null
  ) {
    pixelData ??= [];

    var output = new System.Collections.Generic.List<byte>();

    // Magic
    output.AddRange(BpgFile.Magic);

    // Byte 4: pixel_format(3) | alpha1_flag(1) | bit_depth_minus_8(4)
    var bitDepthMinus8 = bitDepth - 8;
    var byte4 = (byte)((((int)pixelFormat & 0x07) << 5) | ((hasAlpha ? 1 : 0) << 4) | (bitDepthMinus8 & 0x0F));
    output.Add(byte4);

    // Byte 5: color_space(4) | extension_present(1) | alpha2_flag(1) | limited_range(1) | animation_flag(1)
    var byte5 = (byte)(((int)colorSpace & 0x0F) << 4);
    output.Add(byte5);

    // Width, Height, picture_data_length as ue7
    BpgUe7.Write(output, width);
    BpgUe7.Write(output, height);
    BpgUe7.Write(output, pixelData.Length);

    // Pixel data
    output.AddRange(pixelData);

    return output.ToArray();
  }
}
