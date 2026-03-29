using System;
using NUnit.Framework;
using FileFormat.GraphicsMaster;

namespace FileFormat.GraphicsMaster.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new GraphicsMasterFile {
      PixelData = new byte[GraphicsMasterFile.ExpectedFileSize],
    };
    var bytes = GraphicsMasterWriter.ToBytes(original);
    var roundTripped = GraphicsMasterReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => GraphicsMasterReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => GraphicsMasterWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
