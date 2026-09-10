using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bpg;

namespace FileFormat.Bpg.Tests;

[TestFixture]
public sealed class BpgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BpgReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BpgReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bpg"));
    Assert.Throws<FileNotFoundException>(() => BpgReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BpgReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(new byte[3]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(new byte[20]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var data = _BuildMinimalBpg(4, 3, BpgPixelFormat.Grayscale, 8, BpgColorSpace.YCbCrBT601);
    var result = BpgReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(3));
      Assert.That(result.PixelFormat, Is.EqualTo(BpgPixelFormat.Grayscale));
      Assert.That(result.BitDepth, Is.EqualTo(8));
      Assert.That(result.ColorSpace, Is.EqualTo(BpgColorSpace.YCbCrBT601));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesPixelFormat() {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb);
    Assert.That(BpgReader.FromBytes(data).PixelFormat, Is.EqualTo(BpgPixelFormat.YCbCr444));
  }

  [TestCase(10)]
  [TestCase(12)]
  [TestCase(14)]
  [Category("Unit")]
  public void FromBytes_SupportedBitDepth_ParsesCorrectly(int depth) {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr420, depth, BpgColorSpace.YCbCrBT709);
    Assert.That(BpgReader.FromBytes(data).BitDepth, Is.EqualTo(depth));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AlphaFlag_ParsesCorrectly() {
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb, hasAlpha: true);
    Assert.That(BpgReader.FromBytes(data).HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LargeDimensions_Ue7ParsesCorrectly() {
    var data = _BuildMinimalBpg(1920, 1080, BpgPixelFormat.YCbCr420, 8, BpgColorSpace.YCbCrBT709);
    var result = BpgReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(1920));
      Assert.That(result.Height, Is.EqualTo(1080));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_ReadCorrectly() {
    byte[] pixelData = [0xde, 0xad, 0xbe, 0xef];
    var data = _BuildMinimalBpg(2, 2, BpgPixelFormat.Grayscale, 8, BpgColorSpace.YCbCrBT601, pixelData: pixelData);
    Assert.That(BpgReader.FromBytes(data).PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = _BuildMinimalBpg(3, 2, BpgPixelFormat.Grayscale, 8, BpgColorSpace.YCbCrBT601);
    using var stream = new MemoryStream(data);
    var result = BpgReader.FromStream(stream);
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(3));
      Assert.That(result.Height, Is.EqualTo(2));
    });
  }

  [TestCase(6 << 5, TestName = "FromBytes_ReservedPixelFormat_IsRejected")]
  [TestCase(7 << 5, TestName = "FromBytes_SecondReservedPixelFormat_IsRejected")]
  [Category("Unit")]
  public void FromBytes_ReservedPixelFormat_Throws(int byte4) {
    var data = _BuildMinimalBpg(1, 1, BpgPixelFormat.Grayscale, 8, BpgColorSpace.YCbCrBT601);
    data[4] = (byte)byte4;
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReservedColorSpace_Throws() {
    var data = _BuildMinimalBpg(1, 1, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb);
    data[5] = 5 << 4;
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_GrayscaleWithNonzeroColorSpace_Throws() {
    var data = _BuildMinimalBpg(1, 1, BpgPixelFormat.Grayscale, 8, BpgColorSpace.Rgb);
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DepthPastFourteen_Throws() {
    var data = _BuildMinimalBpg(1, 1, BpgPixelFormat.YCbCr444, 15, BpgColorSpace.Rgb);
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(data));
  }

  [TestCase(0, 1, TestName = "FromBytes_ZeroWidth_Throws")]
  [TestCase(1, 0, TestName = "FromBytes_ZeroHeight_Throws")]
  [Category("Unit")]
  public void FromBytes_ZeroDimension_Throws(int width, int height) {
    var data = _BuildMinimalBpg(width, height, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb);
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedDeclaredPictureData_Throws() {
    var data = _BuildMinimalBpg(1, 1, BpgPixelFormat.YCbCr444, 8, BpgColorSpace.Rgb, pixelData: [1, 2]);
    data[^3] = 3; // picture_data_length immediately precedes the two payload bytes
    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedDeclaredExtensionData_Throws() {
    var output = new List<byte>(BpgFile.Magic) {
      (byte)((int)BpgPixelFormat.YCbCr444 << 5),
      (byte)(((int)BpgColorSpace.Rgb << 4) | 0x08),
    };
    BpgUe7.Write(output, 1);
    BpgUe7.Write(output, 1);
    BpgUe7.Write(output, 0);
    BpgUe7.Write(output, 4);
    output.AddRange([1, 2]);

    Assert.Throws<InvalidDataException>(() => BpgReader.FromBytes([.. output]));
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
    var output = new List<byte>();
    output.AddRange(BpgFile.Magic);

    var bitDepthMinus8 = bitDepth - 8;
    output.Add((byte)((((int)pixelFormat & 0x07) << 5) | ((hasAlpha ? 1 : 0) << 4) | (bitDepthMinus8 & 0x0f)));
    output.Add((byte)(((int)colorSpace & 0x0f) << 4));

    BpgUe7.Write(output, width);
    BpgUe7.Write(output, height);
    BpgUe7.Write(output, pixelData.Length);
    output.AddRange(pixelData);
    return [.. output];
  }
}
