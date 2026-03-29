using System;
using NUnit.Framework;
using FileFormat.RagePaint;

namespace FileFormat.RagePaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new RagePaintFile { PixelData = new byte[RagePaintFile.ExpectedFileSize] };
    var bytes = RagePaintWriter.ToBytes(original);
    var roundTripped = RagePaintReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(RagePaintFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => RagePaintReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => RagePaintWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
