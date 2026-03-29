using System;
using NUnit.Framework;
using FileFormat.DigiSpec;

namespace FileFormat.DigiSpec.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new DigiSpecFile {
      Palette = new short[16],
      PixelData = new byte[32000],
    };
    var bytes = DigiSpecWriter.ToBytes(original);
    var roundTripped = DigiSpecReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(32000));
    Assert.That(roundTripped.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => DigiSpecReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => DigiSpecWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
