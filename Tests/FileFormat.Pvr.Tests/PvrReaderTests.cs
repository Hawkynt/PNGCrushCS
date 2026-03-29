using System;
using System.IO;
using FileFormat.Pvr;

namespace FileFormat.Pvr.Tests;

[TestFixture]
public sealed class PvrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PvrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PvrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pvr"));
    Assert.Throws<FileNotFoundException>(() => PvrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PvrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[52];
    bad[0] = 0xFF;
    bad[1] = 0xFF;
    bad[2] = 0xFF;
    bad[3] = 0xFF;
    Assert.Throws<InvalidDataException>(() => PvrReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidPVRTC4_ParsesCorrectly() {
    var data = _BuildMinimalPvr(PvrPixelFormat.PVRTC_4BPP_RGBA, 8, 8, 32);
    var result = PvrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.PixelFormat, Is.EqualTo(PvrPixelFormat.PVRTC_4BPP_RGBA));
    Assert.That(result.CompressedData.Length, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidETC1_ParsesCorrectly() {
    var data = _BuildMinimalPvr(PvrPixelFormat.ETC1, 4, 4, 8);
    var result = PvrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PixelFormat, Is.EqualTo(PvrPixelFormat.ETC1));
    Assert.That(result.CompressedData.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidPvr_ParsesCorrectly() {
    var data = _BuildMinimalPvr(PvrPixelFormat.PVRTC_4BPP_RGB, 4, 4, 8);
    using var ms = new MemoryStream(data);
    var result = PvrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PixelFormat, Is.EqualTo(PvrPixelFormat.PVRTC_4BPP_RGB));
  }

  private static byte[] _BuildMinimalPvr(PvrPixelFormat format, int width, int height, int dataSize) {
    var totalSize = PvrHeader.StructSize + dataSize;
    var data = new byte[totalSize];

    using var ms = new MemoryStream(data);
    using var bw = new BinaryWriter(ms);

    bw.Write(PvrHeader.Magic);          // Version/Magic
    bw.Write(0u);                        // Flags
    bw.Write((ulong)format);             // PixelFormat
    bw.Write(0u);                        // ColorSpace (Linear)
    bw.Write(0u);                        // ChannelType
    bw.Write((uint)height);              // Height
    bw.Write((uint)width);               // Width
    bw.Write(1u);                        // Depth
    bw.Write(1u);                        // Surfaces
    bw.Write(1u);                        // Faces
    bw.Write(1u);                        // MipmapCount
    bw.Write(0u);                        // MetadataSize

    for (var i = 0; i < dataSize; ++i)
      bw.Write((byte)(i % 256));

    return data;
  }
}
