using System;
using NUnit.Framework;
using FileFormat.AtariPicture;

namespace FileFormat.AtariPicture.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariPictureFile { PixelData = new byte[AtariPictureFile.ExpectedFileSize] };
    var bytes = AtariPictureWriter.ToBytes(original);
    var roundTripped = AtariPictureReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(AtariPictureFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => AtariPictureReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => AtariPictureWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
