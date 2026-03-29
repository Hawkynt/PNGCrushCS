using System;
using NUnit.Framework;
using FileFormat.RamBrandt;

namespace FileFormat.RamBrandt.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new RamBrandtFile {
      PixelData = new byte[RamBrandtFile.ExpectedFileSize],
    };
    var bytes = RamBrandtWriter.ToBytes(original);
    var roundTripped = RamBrandtReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => RamBrandtReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => RamBrandtWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
