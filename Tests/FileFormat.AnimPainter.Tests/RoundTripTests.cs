using System;
using NUnit.Framework;
using FileFormat.AnimPainter;

namespace FileFormat.AnimPainter.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var frame = new AnimPainterFrame(
      BitmapData: new byte[8000],
      VideoMatrix: new byte[1000],
      ColorRam: new byte[1000],
      BackgroundColor: 1
    );
    var original = new AnimPainterFile {
      LoadAddress = 0x2000,
      Frames = [frame],
    };
    var bytes = AnimPainterWriter.ToBytes(original);
    var roundTripped = AnimPainterReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.FrameCount, Is.EqualTo(1));
    Assert.That(roundTripped.Frames[0].BitmapData, Is.EqualTo(frame.BitmapData));
    Assert.That(roundTripped.Frames[0].BackgroundColor, Is.EqualTo(frame.BackgroundColor));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AnimPainterReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AnimPainterWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
