using System;
using NUnit.Framework;
using FileFormat.GodPaint;

namespace FileFormat.GodPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new GodPaintFile { PixelData = new byte[GodPaintFile.ExpectedFileSize] };
    var bytes = GodPaintWriter.ToBytes(original);
    var roundTripped = GodPaintReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(GodPaintFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => GodPaintReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => GodPaintWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
