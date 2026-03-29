using System;
using NUnit.Framework;
using FileFormat.Blazing;

namespace FileFormat.Blazing.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new BlazingFile {
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
    };
    var bytes = BlazingWriter.ToBytes(original);
    var roundTripped = BlazingReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(roundTripped.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(roundTripped.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => BlazingReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => BlazingWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
