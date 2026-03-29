using System;
using NUnit.Framework;
using FileFormat.Sprite64;

namespace FileFormat.Sprite64.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new Sprite64File {
      SpriteData = new byte[63],
      ModeByte = 0x00,
    };
    var bytes = Sprite64Writer.ToBytes(original);
    var roundTripped = Sprite64Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.SpriteData, Is.EqualTo(original.SpriteData));
    Assert.That(roundTripped.ModeByte, Is.EqualTo(original.ModeByte));
  }

  [Test]
  [Category("Integration")]
  public void WriteThenRead_MulticolorMode_PreservesModeByte() {
    var original = new Sprite64File {
      SpriteData = new byte[63],
      ModeByte = 0x80,
    };
    var bytes = Sprite64Writer.ToBytes(original);
    var roundTripped = Sprite64Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.IsMulticolor, Is.True);
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => Sprite64Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => Sprite64Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
