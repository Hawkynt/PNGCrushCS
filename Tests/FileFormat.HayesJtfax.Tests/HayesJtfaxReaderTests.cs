using System;
using System.IO;
using FileFormat.HayesJtfax;

namespace FileFormat.HayesJtfax.Tests;

[TestFixture]
public sealed class HayesJtfaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HayesJtfaxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HayesJtfaxReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jtf"));
    Assert.Throws<FileNotFoundException>(() => HayesJtfaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HayesJtfaxReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HayesJtfaxReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[HayesJtfaxFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => HayesJtfaxReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidJtf(16, 4);
    var result = HayesJtfaxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidJtf(24, 3);
    var file = HayesJtfaxReader.FromBytes(original);
    var written = HayesJtfaxWriter.ToBytes(file);
    var reread = HayesJtfaxReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidJtf(8, 2);
    using var ms = new MemoryStream(data);
    var result = HayesJtfaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidJtf(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[HayesJtfaxFile.HeaderSize + pixelDataSize];

    data[0] = 0x4A; // 'J'
    data[1] = 0x54; // 'T'
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)1); // version
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);

    for (var i = 0; i < pixelDataSize; ++i)
      data[HayesJtfaxFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
