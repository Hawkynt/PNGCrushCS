using System;
using System.IO;
using FileFormat.AtariGrafik;

namespace FileFormat.AtariGrafik.Tests;

[TestFixture]
public sealed class AtariGrafikReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AtariGrafikReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AtariGrafikReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pcp"));
    Assert.Throws<FileNotFoundException>(() => AtariGrafikReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AtariGrafikReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AtariGrafikReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AtariGrafikReader.FromBytes(new byte[32035]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[AtariGrafikFile.ExpectedFileSize];

    var result = AtariGrafikReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Palette.Length, Is.EqualTo(16));
      Assert.That(result.PixelData.Length, Is.EqualTo(AtariGrafikFile.PixelDataSize));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var data = new byte[AtariGrafikFile.ExpectedFileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var file = AtariGrafikReader.FromBytes(data);
    var written = AtariGrafikWriter.ToBytes(file);
    var reread = AtariGrafikReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Resolution, Is.EqualTo(file.Resolution));
      Assert.That(reread.Palette, Is.EqualTo(file.Palette));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[AtariGrafikFile.ExpectedFileSize];
    using var ms = new MemoryStream(data);
    var result = AtariGrafikReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
  }
}
