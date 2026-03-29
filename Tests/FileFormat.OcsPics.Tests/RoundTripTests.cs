using System;
using NUnit.Framework;
using FileFormat.OcsPics;

namespace FileFormat.OcsPics.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new OcsPicsFile {
      Palette = new short[16],
      PixelData = new byte[32000],
    };
    var bytes = OcsPicsWriter.ToBytes(original);
    var roundTripped = OcsPicsReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(32000));
    Assert.That(roundTripped.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => OcsPicsReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => OcsPicsWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
