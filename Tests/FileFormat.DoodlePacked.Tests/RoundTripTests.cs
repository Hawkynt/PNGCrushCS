using System;
using NUnit.Framework;
using FileFormat.DoodlePacked;

namespace FileFormat.DoodlePacked.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new DoodlePackedFile {
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
    };
    var bytes = DoodlePackedWriter.ToBytes(original);
    var roundTripped = DoodlePackedReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(roundTripped.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => DoodlePackedReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => DoodlePackedWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
