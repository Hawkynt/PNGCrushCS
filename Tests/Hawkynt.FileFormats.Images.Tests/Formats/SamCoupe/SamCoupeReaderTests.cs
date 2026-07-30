using System;
using System.IO;
using FileFormat.SamCoupe;

namespace FileFormat.SamCoupe.Tests;

[TestFixture]
public sealed class SamCoupeReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SamCoupeReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SamCoupeReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sam"));
    Assert.Throws<FileNotFoundException>(() => SamCoupeReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => SamCoupeReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[24577];
    Assert.Throws<InvalidDataException>(() => SamCoupeReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode4_ParsesCorrectly() {
    var data = new byte[24576];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var result = SamCoupeReader.FromBytes(data, SamCoupeMode.Mode4);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(SamCoupeMode.Mode4));
    Assert.That(result.PixelData.Length, Is.EqualTo(192 * 128));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode3_ParsesDimensions() {
    var data = new byte[24576];

    var result = SamCoupeReader.FromBytes(data, SamCoupeMode.Mode3);

    Assert.That(result.Width, Is.EqualTo(512));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(SamCoupeMode.Mode3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DefaultMode_IsMode4() {
    var data = new byte[24576];

    var result = SamCoupeReader.FromBytes(data);

    Assert.That(result.Mode, Is.EqualTo(SamCoupeMode.Mode4));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SamCoupeReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[24576];
    using var ms = new MemoryStream(data);

    var result = SamCoupeReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.PixelData.Length, Is.EqualTo(192 * 128));
  }
}
