using System;
using System.IO;
using FileFormat.Fli;

namespace FileFormat.Fli.Tests;

[TestFixture]
public sealed class FliReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fli"));
    Assert.Throws<FileNotFoundException>(() => FliReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => FliReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[128];
    bad[4] = 0xFF;
    bad[5] = 0xFF;
    Assert.Throws<InvalidDataException>(() => FliReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFli_ParsesCorrectly() {
    var data = _BuildMinimalFli(320, 200, 1);
    var result = FliReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.FrameCount, Is.EqualTo(1));
    Assert.That(result.FrameType, Is.EqualTo(FliFrameType.Fli));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFlc_ParsesCorrectly() {
    var data = _BuildMinimalFlc(640, 480, 2);
    var result = FliReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(480));
    Assert.That(result.FrameCount, Is.EqualTo(2));
    Assert.That(result.FrameType, Is.EqualTo(FliFrameType.Flc));
  }

  private static byte[] _BuildMinimalFli(int width, int height, int frameCount) =>
    _BuildMinimalFile(width, height, frameCount, unchecked((short)0xAF11));

  private static byte[] _BuildMinimalFlc(int width, int height, int frameCount) =>
    _BuildMinimalFile(width, height, frameCount, unchecked((short)0xAF12));

  private static byte[] _BuildMinimalFile(int width, int height, int frameCount, short magic) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // Header (128 bytes)
    var headerStart = ms.Position;
    bw.Write(0);             // Size (patched later)
    bw.Write(magic);         // Magic
    bw.Write((short)frameCount); // Frames
    bw.Write((short)width);  // Width
    bw.Write((short)height); // Height
    bw.Write((short)8);      // Depth
    bw.Write((short)0);      // Flags
    bw.Write(100);           // Speed
    bw.Write(new byte[108]); // Reserved

    // Write frames with a single Black chunk each
    for (var f = 0; f < frameCount; ++f) {
      var frameStart = ms.Position;
      bw.Write(0);               // Frame size (patched later)
      bw.Write(unchecked((short)0xF1FA)); // Frame magic
      bw.Write((short)1);        // Chunk count
      bw.Write(new byte[8]);     // Reserved

      // Black chunk (type 13, no data)
      bw.Write(6);               // Chunk size (header only)
      bw.Write((short)13);       // Chunk type = Black

      var frameEnd = ms.Position;
      ms.Position = frameStart;
      bw.Write((int)(frameEnd - frameStart));
      ms.Position = frameEnd;
    }

    // Patch file size
    var totalSize = (int)ms.Length;
    ms.Position = 0;
    bw.Write(totalSize);
    ms.Position = totalSize;

    return ms.ToArray();
  }
}
