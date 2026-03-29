using System;
using NUnit.Framework;
using FileFormat.StTrueColor;

namespace FileFormat.StTrueColor.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new StTrueColorFile {
      PixelData = new byte[StTrueColorFile.FileSize],
    };
    var bytes = StTrueColorWriter.ToBytes(original);
    var roundTripped = StTrueColorReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(StTrueColorFile.FileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => StTrueColorReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => StTrueColorWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
