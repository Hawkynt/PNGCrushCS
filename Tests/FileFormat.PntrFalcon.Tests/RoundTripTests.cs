using System;
using NUnit.Framework;
using FileFormat.PntrFalcon;

namespace FileFormat.PntrFalcon.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new PntrFalconFile { PixelData = new byte[PntrFalconFile.ExpectedFileSize] };
    var bytes = PntrFalconWriter.ToBytes(original);
    var roundTripped = PntrFalconReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(PntrFalconFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => PntrFalconReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => PntrFalconWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
