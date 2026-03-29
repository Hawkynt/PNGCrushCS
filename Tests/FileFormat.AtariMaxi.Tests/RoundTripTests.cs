using System;
using NUnit.Framework;
using FileFormat.AtariMaxi;

namespace FileFormat.AtariMaxi.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariMaxiFile {
      PixelData = new byte[AtariMaxiFile.ExpectedFileSize],
    };
    var bytes = AtariMaxiWriter.ToBytes(original);
    var roundTripped = AtariMaxiReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariMaxiReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariMaxiWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
