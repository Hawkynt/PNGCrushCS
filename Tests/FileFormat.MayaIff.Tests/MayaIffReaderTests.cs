using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.MayaIff;

namespace FileFormat.MayaIff.Tests;

[TestFixture]
public sealed class MayaIffReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MayaIffReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MayaIffReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".iff"));
    Assert.Throws<FileNotFoundException>(() => MayaIffReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MayaIffReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => MayaIffReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = _BuildMinimalFile(2, 1, true);
    data[0] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => MayaIffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var data = _BuildMinimalFile(2, 1, true);
    data[8] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => MayaIffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_ParsesDimensions() {
    var data = _BuildMinimalFile(4, 3, true);

    var result = MayaIffReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_ParsesPixelData() {
    var width = 2;
    var height = 1;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var data = _BuildMinimalFile(width, height, true, pixelData);

    var result = MayaIffReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesDimensions() {
    var data = _BuildMinimalFile(5, 2, false);

    var result = MayaIffReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(5));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesPixelData() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var data = _BuildMinimalFile(width, height, false, pixelData);

    var result = MayaIffReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
    Assert.That(result.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgba_Parses() {
    var data = _BuildMinimalFile(2, 2, true);
    using var ms = new MemoryStream(data);

    var result = MayaIffReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.HasAlpha, Is.True);
  }

  /// <summary>Builds a minimal valid Maya IFF byte array for testing.</summary>
  private static byte[] _BuildMinimalFile(int width, int height, bool hasAlpha, byte[]? pixelData = null) {
    var bpp = hasAlpha ? 4 : 3;
    pixelData ??= new byte[width * height * bpp];
    var pixelDataSize = pixelData.Length;
    var pixelPaddedSize = (pixelDataSize + 3) & ~3;

    // FOR4(4) + bodySize(4) + CIMG(4) + TBHD_tag(4) + TBHD_size(4) + TBHD_data(32) + pixel_tag(4) + pixel_size(4) + pixelPaddedData
    var bodySize = 4 + (8 + 32) + (8 + pixelPaddedSize);
    var totalSize = 8 + bodySize;
    var result = new byte[totalSize];
    var span = result.AsSpan();
    var offset = 0;

    Encoding.ASCII.GetBytes("FOR4").CopyTo(span[offset..]);
    offset += 4;
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)bodySize);
    offset += 4;
    Encoding.ASCII.GetBytes("CIMG").CopyTo(span[offset..]);
    offset += 4;

    // TBHD chunk
    Encoding.ASCII.GetBytes("TBHD").CopyTo(span[offset..]);
    offset += 4;
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], 32);
    offset += 4;
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)width);
    offset += 4;
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)height);
    offset += 4;
    BinaryPrimitives.WriteUInt16BigEndian(span[offset..], 1); // prnum
    offset += 2;
    BinaryPrimitives.WriteUInt16BigEndian(span[offset..], 1); // prden
    offset += 2;
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], hasAlpha ? 3u : 0u); // flags
    offset += 4;
    BinaryPrimitives.WriteUInt16BigEndian(span[offset..], 1); // bytes
    offset += 2;
    BinaryPrimitives.WriteUInt16BigEndian(span[offset..], 1); // tiles
    offset += 2;
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], 0); // compression
    offset += 4;
    offset += 8; // reserved

    // Pixel data chunk
    var pixelTag = hasAlpha ? "RGBA" : "RGB ";
    Encoding.ASCII.GetBytes(pixelTag).CopyTo(span[offset..]);
    offset += 4;
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)pixelDataSize);
    offset += 4;
    pixelData.CopyTo(span[offset..]);

    return result;
  }
}
