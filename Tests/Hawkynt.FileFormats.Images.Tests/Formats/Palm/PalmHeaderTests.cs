using System;
using System.Linq;
using FileFormat.Palm;
using FileFormat.Core;

namespace FileFormat.Palm.Tests;

[TestFixture]
public sealed class PalmHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(PalmHeader.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new PalmHeader(
      Width: 320,
      Height: 240,
      BytesPerRow: 320,
      Flags: PalmHeader.FlagCompressed | PalmHeader.FlagHasColorTable,
      BitsPerPixel: 8,
      Version: 1,
      NextDepthOffset: 0,
      TransparentIndex: 5,
      CompressionType: (byte)PalmCompression.Rle,
      Reserved: 0
    );

    var buffer = new byte[PalmHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = PalmHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasEntries() {
    var map = PalmHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);

    Assert.That(map, Has.Length.GreaterThan(0));
    Assert.That(totalSize, Is.EqualTo(PalmHeader.StructSize));
  }
}
