using System;
using NUnit.Framework;
using FileFormat.AtariGraphics11;

namespace FileFormat.AtariGraphics11.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariGraphics11File {
      PixelData = new byte[7680],
    };
    var bytes = AtariGraphics11Writer.ToBytes(original);
    var roundTripped = AtariGraphics11Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariGraphics11Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariGraphics11Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
