using System;
using NUnit.Framework;
using FileFormat.AtariAnimation;

namespace FileFormat.AtariAnimation.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var frame = new byte[AtariAnimationFile.FrameSize];
    var original = new AtariAnimationFile { Frames = [frame] };
    var bytes = AtariAnimationWriter.ToBytes(original);
    var roundTripped = AtariAnimationReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.Frames, Has.Length.EqualTo(1));
    Assert.That(roundTripped.Frames[0], Has.Length.EqualTo(AtariAnimationFile.FrameSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => AtariAnimationReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => AtariAnimationWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
