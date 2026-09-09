using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Ipg.Tests;

[TestFixture]
public sealed class IpgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromSpan_Ipk01_ReadsIndexedEmbeddedBmp() {
    var image = _Image(0x10);
    var file = IpgReader.FromSpan(_V1(image));

    Assert.Multiple(() => {
      Assert.That(file.Version, Is.EqualTo(1));
      Assert.That(file.Images, Has.Count.EqualTo(1));
      Assert.That(file.Images[0].PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Ipk03_ReadsFixedImageRecord() {
    var image = _Image(0x40);
    var file = IpgReader.FromSpan(_V3(image));

    Assert.Multiple(() => {
      Assert.That(file.Version, Is.EqualTo(3));
      Assert.That(file.Images, Has.Count.EqualTo(1));
      Assert.That(file.Images[0].PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Ipk03_OutOfRangeImage_IsRefused() {
    var data = _V3(_Image(0x20));
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(86), uint.MaxValue);

    Assert.That(() => IpgReader.FromSpan(data), Throws.TypeOf<InvalidDataException>());
  }

  private static byte[] _V1(RawImage image) {
    var bmp = BmpWriter.ToBytes(BmpFile.FromRawImage(image));
    const int indexAt = 84;
    const int itemCount = 6;
    const int imageAt = indexAt + itemCount * 12;
    var result = new byte[imageAt + bmp.Length];
    byte[] signature = [0x05, 0x49, 0x50, 0x4B, 0x30, 0x31, 0x01, 0, 0, 0];
    signature.CopyTo(result, 0);
    result[10] = 0;
    result[11] = 0;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(indexAt), imageAt);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(indexAt + 4), checked((uint)bmp.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(indexAt + 8), 0);
    for (var i = 1; i < itemCount; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(indexAt + i * 12 + 8), uint.MaxValue);
    bmp.CopyTo(result, imageAt);
    return result;
  }

  private static byte[] _V3(RawImage image) {
    var bmp = BmpWriter.ToBytes(BmpFile.FromRawImage(image));
    const int fixedHeaderAt = 46;
    const int recordAt = fixedHeaderAt + 40;
    const int imageAt = fixedHeaderAt + 140;
    var result = new byte[imageAt + bmp.Length];
    byte[] signature = [0x05, 0, 0, 0, 0x49, 0, 0x50, 0, 0x4B, 0, 0x30, 0, 0x33, 0, 0x03, 0, 0, 0];
    signature.CopyTo(result, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(recordAt), imageAt);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(recordAt + 4), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(recordAt + 8), checked((uint)bmp.Length));
    bmp.CopyTo(result, imageAt);
    return result;
  }

  private static RawImage _Image(byte seed) => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      seed, (byte)(seed + 1), (byte)(seed + 2), (byte)(seed + 3), (byte)(seed + 4), (byte)(seed + 5),
      (byte)(seed + 6), (byte)(seed + 7), (byte)(seed + 8), (byte)(seed + 9), (byte)(seed + 10), (byte)(seed + 11),
    ],
  };
}
