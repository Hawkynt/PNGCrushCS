using System;
using NUnit.Framework;
using FileFormat.PhotoChrome;

namespace FileFormat.PhotoChrome.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new PhotoChromeFile { PixelData = new byte[PhotoChromeFile.ExpectedFileSize] };
    var bytes = PhotoChromeWriter.ToBytes(original);
    var roundTripped = PhotoChromeReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(PhotoChromeFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => PhotoChromeReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => PhotoChromeWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
