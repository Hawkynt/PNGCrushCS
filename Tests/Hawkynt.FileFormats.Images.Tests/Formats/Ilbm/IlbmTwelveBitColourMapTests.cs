using System;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

/// <summary>
/// Widening a colour map the Amiga wrote four bits at a time.
/// </summary>
/// <remarks>
/// The machine's palette is four bits a channel, and a colour map it writes puts each value in the
/// high nibble and leaves the low one empty. Taken as it stands that makes 8 into 0x80 where the
/// machine shows 0x88 — every colour a little too dark, and the brightest white 0xF0 rather than
/// 0xFF. The picture looks right and no pixel is.
/// <para/>
/// A map whose low nibbles are every one of them zero is such a palette; sixteen colours chosen
/// independently do not all land on a multiple of sixteen by chance. A map with anything in a low
/// nibble is already eight-bit and is left alone.
/// <para/>
/// Checked against RECOIL on real files: two samples that matched 18 and 25 per cent of their pixels
/// now match every one of them.
/// </remarks>
[TestFixture]
public sealed class IlbmTwelveBitColourMapTests {

  /// <summary>Builds the smallest ILBM that carries a colour map and one plane of pixels.</summary>
  private static byte[] _Build(byte[] cmap) {
    var body = new byte[2];
    var size = 4 + (8 + 20) + (8 + cmap.Length) + (8 + body.Length);
    var data = new byte[8 + size];
    var at = 0;

    void Chunk(string id, byte[] payload) {
      System.Text.Encoding.ASCII.GetBytes(id).CopyTo(data, at);
      data[at + 4] = (byte)(payload.Length >> 24);
      data[at + 5] = (byte)(payload.Length >> 16);
      data[at + 6] = (byte)(payload.Length >> 8);
      data[at + 7] = (byte)payload.Length;
      payload.CopyTo(data, at + 8);
      at += 8 + payload.Length;
    }

    System.Text.Encoding.ASCII.GetBytes("FORM").CopyTo(data, 0);
    data[4] = (byte)(size >> 24); data[5] = (byte)(size >> 16);
    data[6] = (byte)(size >> 8); data[7] = (byte)size;
    at = 8;
    System.Text.Encoding.ASCII.GetBytes("ILBM").CopyTo(data, at);
    at += 4;

    var bmhd = new byte[20];
    bmhd[1] = 8;   // eight across
    bmhd[3] = 1;   // one down
    bmhd[8] = 1;   // one plane
    Chunk("BMHD", bmhd);
    Chunk("CMAP", cmap);
    Chunk("BODY", body);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void AMapWithEveryLowNibbleEmptyIsWidened() {
    // 0x80 is the Amiga's 8, which it shows as 0x88.
    var file = IlbmReader.FromBytes(_Build([0x80, 0x60, 0xF0, 0x00, 0x00, 0x00]));

    Assert.That(file.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(file.Palette![0], Is.EqualTo(0x88));
      Assert.That(file.Palette![1], Is.EqualTo(0x66));
      Assert.That(file.Palette![2], Is.EqualTo(0xFF), "the brightest must reach white");
    });
  }

  [Test]
  [Category("Unit")]
  public void AMapThatIsAlreadyEightBitIsLeftAlone() {
    // One low nibble with anything in it says the map is not the Amiga's four-bit kind.
    var file = IlbmReader.FromBytes(_Build([0x80, 0x61, 0xF0, 0x00, 0x00, 0x00]));

    Assert.Multiple(() => {
      Assert.That(file.Palette![0], Is.EqualTo(0x80));
      Assert.That(file.Palette![1], Is.EqualTo(0x61));
    });
  }

  [Test]
  [Category("Unit")]
  public void BlackStaysBlackAndNothingElseMoves() {
    var file = IlbmReader.FromBytes(_Build([0x00, 0x00, 0x00, 0x10, 0x00, 0x00]));

    Assert.Multiple(() => {
      Assert.That(file.Palette![0], Is.Zero);
      Assert.That(file.Palette![3], Is.EqualTo(0x11));
    });
  }
}
