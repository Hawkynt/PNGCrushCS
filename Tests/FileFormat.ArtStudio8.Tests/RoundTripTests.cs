using System;
using NUnit.Framework;
using FileFormat.ArtStudio8;

namespace FileFormat.ArtStudio8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new ArtStudio8File {
      PixelData = new byte[7680],
    };
    var bytes = ArtStudio8Writer.ToBytes(original);
    var roundTripped = ArtStudio8Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => ArtStudio8Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => ArtStudio8Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
