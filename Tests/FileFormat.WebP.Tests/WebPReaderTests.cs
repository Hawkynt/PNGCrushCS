using System;
using System.IO;
using FileFormat.WebP;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class WebPReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WebPReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WebPReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".webp"));
    Assert.Throws<FileNotFoundException>(() => WebPReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => WebPReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    // Valid RIFF header but with "AVI " instead of "WEBP"
    var data = _BuildRiffContainer("AVI ", []);
    Assert.Throws<InvalidDataException>(() => WebPReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoImageChunk_ThrowsInvalidDataException() {
    // Valid RIFF WEBP but no VP8 or VP8L chunk, only an EXIF chunk
    var exifData = new byte[] { 0x00, 0x01, 0x02 };
    var data = _BuildRiffContainer("WEBP", [("EXIF", exifData)]);
    Assert.Throws<InvalidDataException>(() => WebPReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WebPReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void ParseVp8L_ValidData_ExtractsCorrectDimensions() {
    // 4x4 lossless: width-1=3, height-1=3
    // Combined: 3 | (3 << 14) = 0x0000C003
    // LE bytes: [0x03, 0xC0, 0x00, 0x00]
    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };
    var features = WebPReader._ParseVp8L(vp8LData);

    Assert.That(features.Width, Is.EqualTo(4));
    Assert.That(features.Height, Is.EqualTo(4));
    Assert.That(features.IsLossless, Is.True);
    Assert.That(features.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ParseVp8_ValidKeyframe_ExtractsCorrectDimensions() {
    // VP8 keyframe for 4x4 image
    var vp8Data = new byte[] { 0x00, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x04, 0x00, 0x04, 0x00 };
    var features = WebPReader._ParseVp8(vp8Data);

    Assert.That(features.Width, Is.EqualTo(4));
    Assert.That(features.Height, Is.EqualTo(4));
    Assert.That(features.IsLossless, Is.False);
    Assert.That(features.HasAlpha, Is.False);
  }

  /// <summary>Builds a minimal RIFF container with the given form type and chunks.</summary>
  private static byte[] _BuildRiffContainer(string formType, (string id, byte[] data)[] chunks) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // RIFF header placeholder
    bw.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
    bw.Write(0); // size placeholder
    bw.Write(new[] { (byte)formType[0], (byte)formType[1], (byte)formType[2], (byte)formType[3] });

    foreach (var (id, data) in chunks) {
      bw.Write(new[] { (byte)id[0], (byte)id[1], (byte)id[2], (byte)id[3] });
      bw.Write(data.Length);
      bw.Write(data);
      if ((data.Length & 1) != 0)
        bw.Write((byte)0); // word-align
    }

    // Patch file size
    var totalSize = (int)(ms.Length - 8);
    ms.Position = 4;
    bw.Write(totalSize);

    return ms.ToArray();
  }
}
