using System;
using NUnit.Framework;
using FileFormat.FliDesigner2;

namespace FileFormat.FliDesigner2.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new FliDesigner2File {
      LoadAddress = 0x3C00,
      BitmapData = new byte[8000],
      ScreenData = new byte[8000],
      ColorRam = new byte[1000],
      ExtraData = new byte[472],
    };
    var bytes = FliDesigner2Writer.ToBytes(original);
    var roundTripped = FliDesigner2Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(roundTripped.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(roundTripped.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(roundTripped.ColorRam, Is.EqualTo(original.ColorRam));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => FliDesigner2Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => FliDesigner2Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
