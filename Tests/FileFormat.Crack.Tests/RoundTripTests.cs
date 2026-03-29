using System;
using NUnit.Framework;
using FileFormat.Crack;

namespace FileFormat.Crack.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new CrackFile {
      Palette = new short[16],
      PixelData = new byte[32000],
    };
    var bytes = CrackWriter.ToBytes(original);
    var roundTripped = CrackReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(32000));
    Assert.That(roundTripped.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => CrackReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => CrackWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
