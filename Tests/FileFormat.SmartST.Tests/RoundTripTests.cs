using System;
using NUnit.Framework;
using FileFormat.SmartST;

namespace FileFormat.SmartST.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new SmartSTFile { PixelData = new byte[SmartSTFile.ExpectedFileSize] };
    var bytes = SmartSTWriter.ToBytes(original);
    var roundTripped = SmartSTReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(SmartSTFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => SmartSTReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => SmartSTWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
