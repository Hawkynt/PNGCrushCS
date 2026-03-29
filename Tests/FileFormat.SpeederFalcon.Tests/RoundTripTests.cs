using System;
using NUnit.Framework;
using FileFormat.SpeederFalcon;

namespace FileFormat.SpeederFalcon.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new SpeederFalconFile { PixelData = new byte[SpeederFalconFile.ExpectedFileSize] };
    var bytes = SpeederFalconWriter.ToBytes(original);
    var roundTripped = SpeederFalconReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(SpeederFalconFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => SpeederFalconReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => SpeederFalconWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
