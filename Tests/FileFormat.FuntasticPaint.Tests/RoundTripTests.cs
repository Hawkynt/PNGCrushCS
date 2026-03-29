using System;
using NUnit.Framework;
using FileFormat.FuntasticPaint;

namespace FileFormat.FuntasticPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new FuntasticPaintFile {
      PixelData = new byte[FuntasticPaintFile.ExpectedFileSize],
    };
    var bytes = FuntasticPaintWriter.ToBytes(original);
    var roundTripped = FuntasticPaintReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => FuntasticPaintReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => FuntasticPaintWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
