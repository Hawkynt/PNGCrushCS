using System;
using NUnit.Framework;
using FileFormat.SoftwareAutomation;

namespace FileFormat.SoftwareAutomation.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new SoftwareAutomationFile { PixelData = new byte[SoftwareAutomationFile.ExpectedFileSize] };
    var bytes = SoftwareAutomationWriter.ToBytes(original);
    var roundTripped = SoftwareAutomationReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(SoftwareAutomationFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => SoftwareAutomationReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => SoftwareAutomationWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
