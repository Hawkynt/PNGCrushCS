using System;
using NUnit.Framework;
using FileFormat.TriPaint;

namespace FileFormat.TriPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new TriPaintFile { PixelData = new byte[TriPaintFile.ExpectedFileSize] };
    var bytes = TriPaintWriter.ToBytes(original);
    var roundTripped = TriPaintReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Has.Length.EqualTo(TriPaintFile.ExpectedFileSize));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() =>
    Assert.That(() => TriPaintReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());

  [Test]
  public void Writer_NullFile_ThrowsNullReferenceException() =>
    Assert.That(() => TriPaintWriter.ToBytes(null!), Throws.TypeOf<NullReferenceException>());
}
