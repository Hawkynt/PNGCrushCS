using System;
using NUnit.Framework;
using FileFormat.AtariGraphics10;

namespace FileFormat.AtariGraphics10.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariGraphics10File {
      PixelData = new byte[7680],
    };
    var bytes = AtariGraphics10Writer.ToBytes(original);
    var roundTripped = AtariGraphics10Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariGraphics10Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariGraphics10Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
