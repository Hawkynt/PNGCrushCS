using System;
using NUnit.Framework;
using FileFormat.FalconPaint;

namespace FileFormat.FalconPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new FalconPaintFile { PixelData = new byte[FalconPaintFile.ExpectedFileSize] };
    var bytes = FalconPaintWriter.ToBytes(original);
    var roundTripped = FalconPaintReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(FalconPaintFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => FalconPaintReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => FalconPaintWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
