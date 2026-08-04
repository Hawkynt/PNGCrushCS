using System;
using FileFormat.Core;
using FileFormat.Cur;
using FileFormat.Ico;

namespace FileFormat.Cur.Tests;

/// <summary>
/// Building a cursor out of an arbitrary picture.
/// </summary>
/// <remarks>
/// A cursor is an icon whose two otherwise unused directory fields carry the point the pointer
/// actually points at, and whose type word is two rather than one. ImageMagick and IrfanView both
/// read what this produces back to the same pixels.
/// </remarks>
[TestFixture]
public class CurAuthoringTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 4] = 10;
      pixels[i * 4 + 1] = 20;
      pixels[i * 4 + 2] = 30;
      pixels[i * 4 + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CurFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_MakesOneEntryWithItsHotspotAtTheTopLeft() {
    var cursor = CurFile.FromRawImage(_Picture(16, 16));

    Assert.Multiple(() => {
      Assert.That(cursor.Images, Has.Count.EqualTo(1));
      Assert.That(cursor.Images[0].HotspotX, Is.EqualTo(0));
      Assert.That(cursor.Images[0].HotspotY, Is.EqualTo(0));
      Assert.That(cursor.Images[0].BitsPerPixel, Is.EqualTo(32));
    });
  }

  [Test]
  public void ToBytes_SaysItIsACursorRatherThanAnIcon() {
    var bytes = CurWriter.ToBytes(CurFile.FromRawImage(_Picture(16, 16)));

    // The two files are told apart by one word, and nothing else about them differs.
    Assert.That(BitConverter.ToUInt16(bytes, 2), Is.EqualTo(2));
  }

  [Test]
  public void ToBytes_PutsTheHotspotWhereAnIconKeepsItsPlaneCount() {
    var cursor = new CurFile {
      Images = [
        new CurImage {
          Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Bmp,
          Data = CurFile.FromRawImage(_Picture(16, 16)).Images[0].Data,
          HotspotX = 5, HotspotY = 9,
        }
      ]
    };

    var bytes = CurWriter.ToBytes(cursor);

    Assert.Multiple(() => {
      Assert.That(BitConverter.ToUInt16(bytes, 6 + 4), Is.EqualTo(5), "the horizontal hotspot");
      Assert.That(BitConverter.ToUInt16(bytes, 6 + 6), Is.EqualTo(9), "the vertical one");
    });
  }

  [Test]
  public void RoundTrip_ThroughBytesKeepsEveryPixel() {
    var source = _Picture(16, 16);

    var restored = CurFile.ToRawImage(CurReader.FromBytes(CurWriter.ToBytes(CurFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(16));
      Assert.That(restored.Height, Is.EqualTo(16));
      Assert.That(restored.ToBgra32(), Is.EqualTo(source.PixelData));
    });
  }
}
