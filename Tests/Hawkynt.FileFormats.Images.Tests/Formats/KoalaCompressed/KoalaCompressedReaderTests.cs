using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.KoalaCompressed;

namespace FileFormat.KoalaCompressed.Tests;

[TestFixture]
public sealed class KoalaCompressedReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KoalaCompressedReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KoalaCompressedReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gg"));
    Assert.Throws<FileNotFoundException>(() => KoalaCompressedReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KoalaCompressedReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => KoalaCompressedReader.FromBytes(new byte[3]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidCompressedFile(0x6000, 0x03);
    var result = KoalaCompressedReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.VideoMatrix.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = _BuildValidCompressedFile(0x6000, 0x00);
    var result = KoalaCompressedReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidCompressedFile(0x6000, 0x05);
    using var ms = new MemoryStream(data);
    var result = KoalaCompressedReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var videoMatrix = new byte[1000];
    for (var i = 0; i < videoMatrix.Length; ++i)
      videoMatrix[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var original = new KoalaCompressedFile {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = 11,
    };

    var bytes = KoalaCompressedWriter.ToBytes(original);
    var restored = KoalaCompressedReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.VideoMatrix, Is.EqualTo(original.VideoMatrix));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RleCompressesRunsCorrectly() {
    var bitmapData = new byte[8000];
    Array.Fill(bitmapData, (byte)0xAA);

    var original = new KoalaCompressedFile {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = KoalaCompressedWriter.ToBytes(original);

    Assert.That(bytes.Length, Is.LessThan(10003));

    var restored = KoalaCompressedReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
  }

  private static byte[] _BuildValidCompressedFile(ushort loadAddress, byte backgroundColor) {
    var decompressed = new byte[10001];
    for (var i = 0; i < 8000; ++i)
      decompressed[i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      decompressed[8000 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      decompressed[9000 + i] = (byte)((i + 3) % 16);

    decompressed[10000] = backgroundColor;

    var compressed = new List<byte>();
    compressed.Add((byte)(loadAddress & 0xFF));
    compressed.Add((byte)(loadAddress >> 8));

    for (var i = 0; i < decompressed.Length; ++i) {
      var b = decompressed[i];
      if (b == 0xFE) {
        compressed.Add(0xFE);
        compressed.Add(1);
        compressed.Add(0xFE);
      } else
        compressed.Add(b);
    }

    return compressed.ToArray();
  }
}
