using System;
using System.IO;
using System.Text;
using FileFormat.Wal;

namespace FileFormat.Wal.Tests;

[TestFixture]
public sealed class WalReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WalReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WalReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wal"));
    Assert.Throws<FileNotFoundException>(() => WalReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[50];
    Assert.Throws<InvalidDataException>(() => WalReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimension_ThrowsInvalidDataException() {
    var data = _BuildMinimalWal("test", 0, 4, false);
    Assert.Throws<InvalidDataException>(() => WalReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidNoMipmaps_ParsesCorrectly() {
    var data = _BuildMinimalWal("wall01", 4, 4, false);
    var result = WalReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Name, Is.EqualTo("wall01"));
    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.MipMaps, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidWithMipmaps_ParsesAllLevels() {
    var data = _BuildMinimalWal("wall02", 8, 8, true);
    var result = WalReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(64));
    Assert.That(result.MipMaps, Is.Not.Null);
    Assert.That(result.MipMaps!, Has.Length.EqualTo(3));
    Assert.That(result.MipMaps![0].Length, Is.EqualTo(16)); // 4x4
    Assert.That(result.MipMaps[1].Length, Is.EqualTo(4));   // 2x2
    Assert.That(result.MipMaps[2].Length, Is.EqualTo(1));   // 1x1
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidWal_ParsesCorrectly() {
    var data = _BuildMinimalWal("stream_test", 4, 4, false);
    using var ms = new MemoryStream(data);
    var result = WalReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Name, Is.EqualTo("stream_test"));
  }

  private static byte[] _BuildMinimalWal(string name, int width, int height, bool withMips) {
    var mip0Size = width * height;
    var mip0Offset = WalHeader.StructSize;

    var totalSize = mip0Offset + mip0Size;

    uint mip1Offset = 0, mip2Offset = 0, mip3Offset = 0;

    if (withMips && width >= 8 && height >= 8) {
      var mip1Size = (width / 2) * (height / 2);
      var mip2Size = (width / 4) * (height / 4);
      var mip3Size = (width / 8) * (height / 8);

      mip1Offset = (uint)(mip0Offset + mip0Size);
      mip2Offset = (uint)(mip1Offset + mip1Size);
      mip3Offset = (uint)(mip2Offset + mip2Size);
      totalSize = (int)mip3Offset + mip3Size;
    }

    var data = new byte[totalSize];
    using var ms = new MemoryStream(data);
    using var bw = new BinaryWriter(ms);

    var nameBytes = new byte[32];
    var encoded = Encoding.ASCII.GetBytes(name);
    Array.Copy(encoded, nameBytes, Math.Min(encoded.Length, 32));
    bw.Write(nameBytes);

    bw.Write((uint)width);
    bw.Write((uint)height);
    bw.Write((uint)mip0Offset);
    bw.Write(mip1Offset);
    bw.Write(mip2Offset);
    bw.Write(mip3Offset);

    var nextFrameBytes = new byte[32];
    bw.Write(nextFrameBytes);

    bw.Write(0u); // Flags
    bw.Write(0u); // Contents
    bw.Write(0u); // Value

    for (var i = 0; i < totalSize - mip0Offset; ++i)
      bw.Write((byte)(i % 256));

    return data;
  }
}
