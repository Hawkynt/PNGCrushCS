using System;
using System.IO;
using FileFormat.NewsRoom;

namespace FileFormat.NewsRoom.Tests;

[TestFixture]
public sealed class NewsRoomReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NewsRoomReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NewsRoomReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nsr"));
    Assert.Throws<FileNotFoundException>(() => NewsRoomReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NewsRoomReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NewsRoomReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NewsRoomReader.FromBytes(new byte[7681]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[NewsRoomFile.ExpectedFileSize];

    var result = NewsRoomReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(192));
      Assert.That(result.PixelData.Length, Is.EqualTo(NewsRoomFile.ExpectedFileSize));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var data = new byte[NewsRoomFile.ExpectedFileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var file = NewsRoomReader.FromBytes(data);
    var written = NewsRoomWriter.ToBytes(file);
    var reread = NewsRoomReader.FromBytes(written);

    Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[NewsRoomFile.ExpectedFileSize];
    using var ms = new MemoryStream(data);
    var result = NewsRoomReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
  }
}
