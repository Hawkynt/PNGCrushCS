using System;
using NUnit.Framework;
using FileFormat.MicroPainter8;

namespace FileFormat.MicroPainter8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new MicroPainter8File {
      PixelData = new byte[7680],
    };
    var bytes = MicroPainter8Writer.ToBytes(original);
    var roundTripped = MicroPainter8Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => MicroPainter8Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => MicroPainter8Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
