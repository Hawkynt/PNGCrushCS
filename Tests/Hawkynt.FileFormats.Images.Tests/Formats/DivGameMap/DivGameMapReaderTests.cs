using System;
using System.IO;
using FileFormat.DivGameMap;

namespace FileFormat.DivGameMap.Tests;

[TestFixture]
public sealed class DivGameMapReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DivGameMapReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DivGameMapReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpg"));
    Assert.Throws<FileNotFoundException>(() => DivGameMapReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DivGameMapReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => DivGameMapReader.FromBytes(new byte[10]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[DivGameMapFile.MinFileSize + DivGameMapFile.EntryHeaderSize + 4];
    Assert.Throws<InvalidDataException>(() => DivGameMapReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidFpg(4, 2);
    var result = DivGameMapReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
      Assert.That(result.Palette.Length, Is.EqualTo(768));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidFpg(3, 3);
    var file = DivGameMapReader.FromBytes(original);
    var written = DivGameMapWriter.ToBytes(file);
    var reread = DivGameMapReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
      Assert.That(reread.Palette, Is.EqualTo(file.Palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidFpg(2, 2);
    using var ms = new MemoryStream(data);
    var result = DivGameMapReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(2));
    });
  }

  private static byte[] _BuildValidFpg(int width, int height) {
    var pixelCount = width * height;
    var totalSize = DivGameMapFile.MagicSize + DivGameMapFile.PaletteSize + DivGameMapFile.EntryHeaderSize + pixelCount;
    var data = new byte[totalSize];
    var offset = 0;

    data[offset++] = 0x66; // 'f'
    data[offset++] = 0x70; // 'p'
    data[offset++] = 0x67; // 'g'
    data[offset++] = 0x1A;

    // Palette: simple grayscale ramp
    for (var i = 0; i < 256; ++i) {
      data[offset++] = (byte)i;
      data[offset++] = (byte)i;
      data[offset++] = (byte)i;
    }

    // Entry header
    offset += 4; // code
    BitConverter.TryWriteBytes(new Span<byte>(data, offset, 4), pixelCount);
    offset += 4; // length
    offset += 32; // description
    offset += 12; // filename
    BitConverter.TryWriteBytes(new Span<byte>(data, offset, 4), width);
    offset += 4;
    BitConverter.TryWriteBytes(new Span<byte>(data, offset, 4), height);
    offset += 4;
    offset += 4; // numPoints = 0

    // Pixel data
    for (var i = 0; i < pixelCount; ++i)
      data[offset + i] = (byte)(i % 256);

    return data;
  }
}
