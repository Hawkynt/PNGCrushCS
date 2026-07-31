using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Pcd;

namespace FileFormat.Pcd.Tests;

[TestFixture]
public sealed class PcdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pcd"));
    Assert.Throws<FileNotFoundException>(() => PcdReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[2059];
    Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[PcdFile.HeaderSize + 3];
    data[PcdFile.PreambleSize] = (byte)'X';
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PcdFile.PreambleSize + 8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(PcdFile.PreambleSize + 10), 1);
    Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(data));
  }

  /// <summary>
  /// Photo CD is fixed-resolution: the Base image is always 768x512 and the file records no
  /// dimensions anywhere. The tests below used to invent a width and height just after the magic and
  /// interleave RGB, which is not the format — they now build a real Base image.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_BaseImage_IsAlwaysTheBaseResolution() {
    var data = _CreateBaseImage(luma: 128, cb: 156, cr: 137);

    var result = PcdReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(768));
      Assert.That(result.Height, Is.EqualTo(512));
    });
  }

  /// <summary>Neutral chroma leaves the luma as a grey, which is the one exactly checkable case.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_NeutralChroma_YieldsGrey() {
    var result = PcdReader.FromBytes(_CreateBaseImage(luma: 128, cb: 156, cr: 137));

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(128), "red");
      Assert.That(result.PixelData[1], Is.EqualTo(128), "green");
      Assert.That(result.PixelData[2], Is.EqualTo(128), "blue");
    });
  }

  /// <summary>A file that stops before its Base image is bad data, not a silent short read.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedBaseImage_ThrowsInvalidDataException() {
    var data = _CreateBaseImage(128, 156, 137);
    Array.Resize(ref data, data.Length - 1024);

    Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(data));
  }

  /// <summary>
  /// A Base image: the magic at 2048, then row groups of two luma rows, a Cb row and a Cr row.
  /// </summary>
  private static byte[] _CreateBaseImage(byte luma, byte cb, byte cr) {
    const int width = 768, height = 512, chromaWidth = width / 2, offset = 0x30000;
    const int groupSize = (width * 2) + (chromaWidth * 2);
    var data = new byte[offset + (groupSize * (height / 2))];

    "PCD_IPI"u8.ToArray().CopyTo(data, 2048);

    for (var group = 0; group < height / 2; ++group) {
      var at = offset + (group * groupSize);
      for (var i = 0; i < width * 2; ++i)
        data[at + i] = luma;

      for (var i = 0; i < chromaWidth; ++i) {
        data[at + (width * 2) + i] = cb;
        data[at + (width * 2) + chromaWidth + i] = cr;
      }
    }

    return data;
  }
}
