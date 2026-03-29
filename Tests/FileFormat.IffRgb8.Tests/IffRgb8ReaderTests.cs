using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.IffRgb8;

namespace FileFormat.IffRgb8.Tests;

[TestFixture]
public sealed class IffRgb8ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgb8Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgb8Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rgb8"));
    Assert.Throws<FileNotFoundException>(() => IffRgb8Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgb8Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => IffRgb8Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[12];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'Z';
    Assert.Throws<InvalidDataException>(() => IffRgb8Reader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var bad = new byte[40];
    bad[0] = (byte)'F'; bad[1] = (byte)'O'; bad[2] = (byte)'R'; bad[3] = (byte)'M';
    BinaryPrimitives.WriteInt32BigEndian(bad.AsSpan(4), 20);
    bad[8] = (byte)'I'; bad[9] = (byte)'L'; bad[10] = (byte)'B'; bad[11] = (byte)'M';
    Assert.Throws<InvalidDataException>(() => IffRgb8Reader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb8_ParsesCorrectly() {
    var original = new IffRgb8File {
      Width = 2,
      Height = 2,
      Compression = IffRgb8Compression.None,
      PixelData = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x80, 0x80, 0x80],
    };

    var data = IffRgb8Writer.ToBytes(original);
    var result = IffRgb8Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.Compression, Is.EqualTo(IffRgb8Compression.None));
      Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
      Assert.That(result.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidByteRun1_ParsesCorrectly() {
    var original = new IffRgb8File {
      Width = 4,
      Height = 2,
      Compression = IffRgb8Compression.ByteRun1,
      PixelData = new byte[4 * 2 * 3],
    };

    for (var i = 0; i < original.PixelData.Length; ++i)
      original.PixelData[i] = (byte)(i * 17 % 256);

    var data = IffRgb8Writer.ToBytes(original);
    var result = IffRgb8Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.Compression, Is.EqualTo(IffRgb8Compression.ByteRun1));
      Assert.That(result.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb8_ParsesCorrectly() {
    var original = new IffRgb8File {
      Width = 2,
      Height = 1,
      Compression = IffRgb8Compression.None,
      PixelData = [0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33],
    };

    var data = IffRgb8Writer.ToBytes(original);
    using var ms = new MemoryStream(data);
    var result = IffRgb8Reader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(1));
      Assert.That(result.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
