using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Tiny;

namespace FileFormat.Tiny.Tests;

[TestFixture]
public sealed class TinyReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TinyReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TinyReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tny"));
    Assert.Throws<FileNotFoundException>(() => TinyReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => TinyReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidLow_ParsesCorrectly() {
    var data = _BuildTiny(TinyResolution.Low, 4, 4000);
    var result = TinyReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(TinyResolution.Low));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palette, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMedium_ParsesCorrectly() {
    var data = _BuildTiny(TinyResolution.Medium, 2, 8000);
    var result = TinyReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(TinyResolution.Medium));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHigh_ParsesCorrectly() {
    var data = _BuildTiny(TinyResolution.High, 1, 16000);
    var result = TinyReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.Resolution, Is.EqualTo(TinyResolution.High));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidLow_ParsesCorrectly() {
    var data = _BuildTiny(TinyResolution.Low, 4, 4000);
    using var stream = new MemoryStream(data);
    var result = TinyReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(TinyResolution.Low));
    });
  }

  private static byte[] _BuildTiny(TinyResolution resolution, int planeCount, int wordsPerPlane) {
    var pixelData = new byte[planeCount * wordsPerPlane * 2];
    var compressed = TinyCompressor.Compress(pixelData, planeCount, wordsPerPlane);

    using var ms = new MemoryStream();
    ms.WriteByte((byte)resolution);

    Span<byte> buf = stackalloc byte[2];
    for (var i = 0; i < 16; ++i) {
      BinaryPrimitives.WriteInt16BigEndian(buf, 0);
      ms.Write(buf);
    }

    ms.Write(compressed);
    return ms.ToArray();
  }
}
