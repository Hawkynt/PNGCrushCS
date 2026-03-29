using System;
using FileFormat.SeattleFilmWorks;

namespace FileFormat.SeattleFilmWorks.Tests;

[TestFixture]
public sealed class SeattleFilmWorksWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SeattleFilmWorksWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PrependsSfwMagic() {
    var file = new SeattleFilmWorksFile {
      JpegData = [0xFF, 0xD8, 0xFF, 0xD9]
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x53)); // 'S'
    Assert.That(bytes[1], Is.EqualTo(0x46)); // 'F'
    Assert.That(bytes[2], Is.EqualTo(0x57)); // 'W'
    Assert.That(bytes[3], Is.EqualTo(0x39)); // '9'
    Assert.That(bytes[4], Is.EqualTo(0x34)); // '4'
    Assert.That(bytes[5], Is.EqualTo(0x41)); // 'A'
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_JpegDataFollowsMagic() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
    var file = new SeattleFilmWorksFile {
      JpegData = jpegData
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(file);

    Assert.That(bytes[6], Is.EqualTo(0xFF));
    Assert.That(bytes[7], Is.EqualTo(0xD8));
    Assert.That(bytes[8], Is.EqualTo(0xFF));
    Assert.That(bytes[9], Is.EqualTo(0xE0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalLength_MagicPlusJpeg() {
    var jpegData = new byte[100];
    var file = new SeattleFilmWorksFile {
      JpegData = jpegData
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6 + 100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyJpegData_ProducesOnlyMagic() {
    var file = new SeattleFilmWorksFile {
      JpegData = []
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6));
    Assert.That(bytes[0], Is.EqualTo(0x53));
    Assert.That(bytes[5], Is.EqualTo(0x41));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_JpegDataPreserved() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xAA, 0xBB, 0xCC, 0xFF, 0xD9 };
    var file = new SeattleFilmWorksFile {
      JpegData = jpegData
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(file);

    for (var i = 0; i < jpegData.Length; ++i)
      Assert.That(bytes[6 + i], Is.EqualTo(jpegData[i]));
  }
}
