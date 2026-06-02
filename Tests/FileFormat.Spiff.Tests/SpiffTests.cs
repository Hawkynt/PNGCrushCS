using System;
using System.IO;
using FileFormat.Spiff;

namespace FileFormat.Spiff.Tests;

[TestFixture]
public sealed class SpiffTests {

  private static SpiffFile _Build() => new() {
    ProfileId = 0,
    ComponentCount = 3,
    Width = 320,
    Height = 240,
    ColorSpace = 10,    // RGB
    BitsPerSample = 8,
    CompressionType = 5, // JPEG
    CompressedPayload = [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x42],
  };

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SpiffReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spf"));
    Assert.Throws<FileNotFoundException>(() => SpiffReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SpiffReader.FromBytes(new byte[10]));

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingSoi_ThrowsInvalidDataException() {
    var bad = new byte[64];
    bad[0] = 0xAA; bad[1] = 0xBB;
    Assert.Throws<InvalidDataException>(() => SpiffReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RoundTrip_PreservesAllFields() {
    var original = _Build();
    var bytes = SpiffWriter.ToBytes(original);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[1], Is.EqualTo(0xD8));
    Assert.That(bytes[2], Is.EqualTo(0xFF));
    Assert.That(bytes[3], Is.EqualTo(0xE8));

    var loaded = SpiffReader.FromSpan(bytes);
    Assert.That(loaded.ProfileId, Is.EqualTo(original.ProfileId));
    Assert.That(loaded.ComponentCount, Is.EqualTo(original.ComponentCount));
    Assert.That(loaded.Width, Is.EqualTo(original.Width));
    Assert.That(loaded.Height, Is.EqualTo(original.Height));
    Assert.That(loaded.ColorSpace, Is.EqualTo(original.ColorSpace));
    Assert.That(loaded.BitsPerSample, Is.EqualTo(original.BitsPerSample));
    Assert.That(loaded.CompressionType, Is.EqualTo(original.CompressionType));
    Assert.That(loaded.CompressedPayload, Is.EqualTo(original.CompressedPayload));
  }
}
