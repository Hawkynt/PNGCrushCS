using System;
using NUnit.Framework;
using FileFormat.CinemasterAtari;

namespace FileFormat.CinemasterAtari.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var frame = new byte[CinemasterAtariFile.FrameSize];
    var original = new CinemasterAtariFile { FrameCount = 1, Frames = [frame] };
    var bytes = CinemasterAtariWriter.ToBytes(original);
    var roundTripped = CinemasterAtariReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.FrameCount, Is.EqualTo(1));
    Assert.That(roundTripped.Frames, Has.Length.EqualTo(1));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => CinemasterAtariReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => CinemasterAtariWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
