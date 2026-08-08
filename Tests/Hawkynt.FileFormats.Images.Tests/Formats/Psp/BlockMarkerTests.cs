using System;
using FileFormat.Core;
using FileFormat.Psp;

namespace FileFormat.Psp.Tests;

/// <summary>
/// Every block of a Paint Shop Pro file opens with its own four-byte marker.
/// </summary>
/// <remarks>
/// The reader read the block identifier where that marker stands and the writer left the marker out,
/// so the two agreed with each other and with nothing else. Both real files in the corpus came back
/// as 4932222 by 1572874 and were refused for being too large, which is the only reason it surfaced
/// at all — a smaller misreading would have drawn a picture and said nothing.
/// </remarks>
[TestFixture]
public sealed class BlockMarkerTests {

  private static readonly byte[] _Marker = [0x7E, 0x42, 0x4B, 0x00];

  private static RawImage _Picture(int width = 13, int height = 7) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void EveryBlockOpensWithTheMarker() {
    var bytes = PspWriter.ToBytes(PspFile.FromRawImage(_Picture()));

    // Walk it the way the format says, and require the chain to land exactly on the end.
    var offset = 36;
    var blocks = 0;
    while (offset + 10 <= bytes.Length) {
      Assert.That(bytes[offset..(offset + 4)], Is.EqualTo(_Marker), $"block {blocks} does not open with the marker");
      var length = BitConverter.ToUInt32(bytes, offset + 6);
      offset += 10 + (int)length;
      ++blocks;
    }

    Assert.Multiple(() => {
      Assert.That(blocks, Is.GreaterThanOrEqualTo(2), "a file states its attributes and its picture");
      Assert.That(offset, Is.EqualTo(bytes.Length), "the blocks account for the file exactly");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheSizeComesBack() {
    var restored = PspReader.FromBytes(PspWriter.ToBytes(PspFile.FromRawImage(_Picture(13, 7))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(13));
      Assert.That(restored.Height, Is.EqualTo(7));
    });
  }
}
