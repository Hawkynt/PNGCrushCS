using System;
using NUnit.Framework;
using FileFormat.Cheese;

namespace FileFormat.Cheese.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new CheeseFile {
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BorderColor = 0,
      BackgroundColor = 1,
      Padding = new byte[14],
    };
    var bytes = CheeseWriter.ToBytes(original);
    var roundTripped = CheeseReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(roundTripped.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(roundTripped.VideoMatrix, Is.EqualTo(original.VideoMatrix));
    Assert.That(roundTripped.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(roundTripped.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => CheeseReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => CheeseWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
