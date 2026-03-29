using System;
using NUnit.Framework;
using FileFormat.HighResAtari;

namespace FileFormat.HighResAtari.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new HighResAtariFile {
      PixelData = new byte[7680],
    };
    var bytes = HighResAtariWriter.ToBytes(original);
    var roundTripped = HighResAtariReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => HighResAtariReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => HighResAtariWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
