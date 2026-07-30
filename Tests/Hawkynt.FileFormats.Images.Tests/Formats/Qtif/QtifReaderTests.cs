using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Qtif;

namespace FileFormat.Qtif.Tests;

[TestFixture]
public sealed class QtifReaderTests {

  private static byte[] BuildQtif(int width, int height, byte[]? pixelData = null) {
    pixelData ??= new byte[width * height * 3];
    var file = new QtifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    return QtifWriter.ToBytes(file);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QtifReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QtifReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".qtif"));
    Assert.Throws<FileNotFoundException>(() => QtifReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QtifReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[] { 0, 0, 0, 1, 0x69, 0x64 };
    Assert.Throws<InvalidDataException>(() => QtifReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoIdatAtom_ThrowsInvalidDataException() {
    var data = new byte[8 + 86];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), (uint)(8 + 86));
    Encoding.ASCII.GetBytes("idsc", data.AsSpan(4));
    Assert.Throws<InvalidDataException>(() => QtifReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsing_DimensionsCorrect() {
    var data = BuildQtif(4, 2);
    var result = QtifReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsing_PixelDataPreserved() {
    var pixels = new byte[3 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var data = BuildQtif(3, 2, pixels);
    var result = QtifReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsing() {
    var data = BuildQtif(2, 2);
    using var ms = new MemoryStream(data);
    var result = QtifReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidAtomSize_ThrowsInvalidDataException() {
    var data = new byte[16];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 4);
    Encoding.ASCII.GetBytes("idat", data.AsSpan(4));
    Assert.Throws<InvalidDataException>(() => QtifReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_IdscTooSmall_ThrowsInvalidDataException() {
    var data = new byte[8 + 10 + 8 + 3];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 8 + 10);
    Encoding.ASCII.GetBytes("idsc", data.AsSpan(4));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(18), 8 + 3);
    Encoding.ASCII.GetBytes("idat", data.AsSpan(22));
    Assert.Throws<InvalidDataException>(() => QtifReader.FromBytes(data));
  }
}
