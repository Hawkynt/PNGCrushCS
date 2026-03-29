using System;
using NUnit.Framework;
using FileFormat.Interlace8;

namespace FileFormat.Interlace8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new Interlace8File {
      Frame1Data = new byte[Interlace8File.FrameSize],
      Frame2Data = new byte[Interlace8File.FrameSize],
    };
    var bytes = Interlace8Writer.ToBytes(original);
    var roundTripped = Interlace8Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.Frame1Data, Is.EqualTo(original.Frame1Data));
    Assert.That(roundTripped.Frame2Data, Is.EqualTo(original.Frame2Data));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => Interlace8Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => Interlace8Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
