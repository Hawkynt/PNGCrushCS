using System;
using NUnit.Framework;
using FileFormat.VidiChrome;

namespace FileFormat.VidiChrome.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new VidiChromeFile { PixelData = new byte[VidiChromeFile.ExpectedFileSize] };
    var bytes = VidiChromeWriter.ToBytes(original);
    var roundTripped = VidiChromeReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(VidiChromeFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => VidiChromeReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => VidiChromeWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
