using System;
using NUnit.Framework;
using FileFormat.AtariAnticMode;

namespace FileFormat.AtariAnticMode.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariAnticModeFile {
      PixelData = new byte[AtariAnticModeFile.ScreenDataSize],
      Mode = AtariAnticModeFile.ModeF,
    };
    var bytes = AtariAnticModeWriter.ToBytes(original);
    var roundTripped = AtariAnticModeReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.Mode, Is.EqualTo(AtariAnticModeFile.ModeF));
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(AtariAnticModeFile.ScreenDataSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => AtariAnticModeReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => AtariAnticModeWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
