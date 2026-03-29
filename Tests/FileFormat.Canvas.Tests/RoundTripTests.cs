using System;
using NUnit.Framework;
using FileFormat.Canvas;

namespace FileFormat.Canvas.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new CanvasFile {
      Resolution = 0,
      Palette = new short[16],
      PixelData = new byte[32000],
    };
    var bytes = CanvasWriter.ToBytes(original);
    var roundTripped = CanvasReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(32000));
    Assert.That(roundTripped.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => CanvasReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() =>
    Assert.That(() => CanvasWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
}
