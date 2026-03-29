using System;
using NUnit.Framework;
using FileFormat.AtariCAD;

namespace FileFormat.AtariCAD.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariCADFile {
      PixelData = new byte[AtariCADFile.ExpectedFileSize],
    };
    var bytes = AtariCADWriter.ToBytes(original);
    var roundTripped = AtariCADReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariCADReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariCADWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
