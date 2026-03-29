using System;
using NUnit.Framework;
using FileFormat.GreatPaint;

namespace FileFormat.GreatPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new GreatPaintFile {
      PixelData = new byte[GreatPaintFile.ExpectedFileSize],
    };
    var bytes = GreatPaintWriter.ToBytes(original);
    var roundTripped = GreatPaintReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => GreatPaintReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => GreatPaintWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
