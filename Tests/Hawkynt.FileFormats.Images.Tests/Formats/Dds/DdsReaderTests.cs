using System;
using System.IO;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class DdsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DdsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DdsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dds"));
    Assert.Throws<FileNotFoundException>(() => DdsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DdsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => DdsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[128];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => DdsReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_ParsesCorrectly() {
    var dds = _BuildMinimalRgbaDds(4, 4);
    var result = DdsReader.FromBytes(dds);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Format, Is.EqualTo(DdsFormat.Rgba));
    Assert.That(result.MipMapCount, Is.EqualTo(1));
    Assert.That(result.Surfaces, Has.Count.EqualTo(1));
    Assert.That(result.Surfaces[0].Data.Length, Is.EqualTo(4 * 4 * 4));
  }

  private static byte[] _BuildMinimalRgbaDds(int width, int height) {
    var pixelDataSize = width * height * 4;
    var fileSize = 4 + DdsHeader.StructSize + pixelDataSize;
    var data = new byte[fileSize];

    using var ms = new MemoryStream(data);
    using var bw = new BinaryWriter(ms);

    // Magic
    bw.Write(0x20534444); // "DDS "

    // DDS_HEADER
    bw.Write(124);        // Size
    bw.Write(0x1 | 0x2 | 0x4 | 0x1000); // Flags: CAPS|HEIGHT|WIDTH|PIXELFORMAT
    bw.Write(height);     // Height
    bw.Write(width);      // Width
    bw.Write(0);          // PitchOrLinearSize
    bw.Write(0);          // Depth
    bw.Write(1);          // MipMapCount
    for (var i = 0; i < 11; ++i)
      bw.Write(0);        // Reserved1[11]

    // DDS_PIXELFORMAT
    bw.Write(32);         // Size
    bw.Write(0x40 | 0x1); // Flags: DDPF_RGB | DDPF_ALPHAPIXELS
    bw.Write(0);          // FourCC (none)
    bw.Write(32);         // RGBBitCount
    bw.Write(0x00FF0000); // RBitMask
    bw.Write(0x0000FF00); // GBitMask
    bw.Write(0x000000FF); // BBitMask
    bw.Write(unchecked((int)0xFF000000)); // ABitMask

    // Caps
    bw.Write(0x1000);     // DDSCAPS_TEXTURE
    bw.Write(0);          // Caps2
    bw.Write(0);          // Caps3
    bw.Write(0);          // Caps4
    bw.Write(0);          // Reserved2

    // Pixel data
    for (var i = 0; i < pixelDataSize; ++i)
      bw.Write((byte)(i % 256));

    return data;
  }
}
