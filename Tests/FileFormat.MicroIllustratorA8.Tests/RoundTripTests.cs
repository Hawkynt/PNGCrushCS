using System;
using NUnit.Framework;
using FileFormat.MicroIllustratorA8;

namespace FileFormat.MicroIllustratorA8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new MicroIllustratorA8File {
      PixelData = new byte[MicroIllustratorA8File.ExpectedFileSize],
    };
    var bytes = MicroIllustratorA8Writer.ToBytes(original);
    var roundTripped = MicroIllustratorA8Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => MicroIllustratorA8Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => MicroIllustratorA8Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
