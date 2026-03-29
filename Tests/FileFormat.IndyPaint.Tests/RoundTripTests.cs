using System;
using NUnit.Framework;
using FileFormat.IndyPaint;

namespace FileFormat.IndyPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new IndyPaintFile { PixelData = new byte[IndyPaintFile.ExpectedFileSize] };
    var bytes = IndyPaintWriter.ToBytes(original);
    var roundTripped = IndyPaintReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(IndyPaintFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => IndyPaintReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => IndyPaintWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
