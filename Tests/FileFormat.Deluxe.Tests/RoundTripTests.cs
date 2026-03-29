using System;
using NUnit.Framework;
using FileFormat.Deluxe;

namespace FileFormat.Deluxe.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new DeluxeFile {
      Resolution = 0,
      Palette = new short[16],
      PixelData = new byte[32000],
    };
    var bytes = DeluxeWriter.ToBytes(original);
    var roundTripped = DeluxeReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(32000));
    Assert.That(roundTripped.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => DeluxeReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => DeluxeWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
