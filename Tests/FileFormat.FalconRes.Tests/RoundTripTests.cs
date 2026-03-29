using System;
using NUnit.Framework;
using FileFormat.FalconRes;

namespace FileFormat.FalconRes.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new FalconResFile { PixelData = new byte[FalconResFile.ExpectedFileSize] };
    var bytes = FalconResWriter.ToBytes(original);
    var roundTripped = FalconResReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(FalconResFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => FalconResReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => FalconResWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
