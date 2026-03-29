using System;
using NUnit.Framework;
using FileFormat.SuperHires;

namespace FileFormat.SuperHires.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new SuperHiresFile {
      LoadAddress = 0x2000,
      BitmapData1 = new byte[8000],
      ScreenData1 = new byte[1000],
      BitmapData2 = new byte[8000],
      ScreenData2 = new byte[1000],
      Padding = new byte[240],
    };
    var bytes = SuperHiresWriter.ToBytes(original);
    var roundTripped = SuperHiresReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(roundTripped.BitmapData1, Is.EqualTo(original.BitmapData1));
    Assert.That(roundTripped.ScreenData1, Is.EqualTo(original.ScreenData1));
    Assert.That(roundTripped.BitmapData2, Is.EqualTo(original.BitmapData2));
    Assert.That(roundTripped.ScreenData2, Is.EqualTo(original.ScreenData2));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => SuperHiresReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => SuperHiresWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
