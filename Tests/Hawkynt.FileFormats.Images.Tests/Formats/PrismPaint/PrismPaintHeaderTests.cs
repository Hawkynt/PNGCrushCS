using System;
using FileFormat.Core;
using FileFormat.PrismPaint;

namespace FileFormat.PrismPaint.Tests;

/// <summary>
/// Where a Prism Paint picture states its size, and what order its palette is in.
/// </summary>
/// <remarks>
/// The size used to be read from the file's first four bytes, which are the signature: a real file
/// came back 20048 by 84, because that is what <c>PNT\0</c> reads as two little-endian words. The
/// size is two big-endian words further in, with the plane count after them.
/// <para/>
/// The palette is three words an entry on the VDI's nought-to-a-thousand scale, and its entries are
/// in the VDI's order rather than the one the pixels index. Leaving them where they lie draws the
/// picture in the right colours put on the wrong shapes — the sample's white outline came out purple
/// while both palettes held exactly the same sixteen colours, which is what made it hard to see.
/// <para/>
/// Checked against RECOIL: all 64000 pixels of the sample match.
/// </remarks>
[TestFixture]
public sealed class PrismPaintHeaderTests {

  /// <summary>Builds a picture of the given size and plane count, with a palette and blank screen.</summary>
  private static byte[] _Build(int width, int height, int planes, params int[] vdiEntries) {
    var colors = 1 << planes;
    var screen = (width + 15) / 16 * 2 * planes * height;
    var data = new byte[128 + colors * 6 + screen];

    "PNT\0"u8.CopyTo(data);
    data[8] = (byte)(width >> 8); data[9] = (byte)width;
    data[10] = (byte)(height >> 8); data[11] = (byte)height;
    data[12] = (byte)(planes >> 8); data[13] = (byte)planes;

    for (var i = 0; i < colors && i * 3 < vdiEntries.Length; ++i)
      for (var channel = 0; channel < 3; ++channel) {
        var value = vdiEntries[i * 3 + channel];
        data[128 + i * 6 + channel * 2] = (byte)(value >> 8);
        data[128 + i * 6 + channel * 2 + 1] = (byte)value;
      }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void TheSizeIsReadFromTheHeaderAndNotFromTheSignature() {
    var file = PrismPaintReader.FromBytes(_Build(320, 200, 4));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320), "reading the signature gives 20048");
      Assert.That(file.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void APaletteChannelRunsToAThousandAndNotTo255() {
    // The VDI's first entry is white at full on every channel.
    var file = PrismPaintReader.FromBytes(_Build(16, 1, 4, 1000, 1000, 1000));

    Assert.That(file.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(file.Palette![AtariStGraphics.VdiToHardwareIndex(0, 4) * 3], Is.EqualTo(255));
      Assert.That(file.Palette![AtariStGraphics.VdiToHardwareIndex(0, 4) * 3 + 1], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheEntriesLandWhereThePixelsLookForThem() {
    // The VDI's second entry is the one the pixels index last, not second.
    var file = PrismPaintReader.FromBytes(_Build(16, 1, 4, 0, 0, 0, 1000, 0, 0));
    var slot = AtariStGraphics.VdiToHardwareIndex(1, 4);

    Assert.Multiple(() => {
      Assert.That(slot, Is.Not.EqualTo(1), "the two orders are not the same");
      Assert.That(file.Palette![slot * 3], Is.EqualTo(255), "the red entry belongs in the VDI's slot");
    });
  }

  [Test]
  [Category("Unit")]
  public void SomethingWithoutTheSignatureIsRefused()
    => Assert.Throws<System.IO.InvalidDataException>(() => PrismPaintReader.FromBytes(new byte[4096]));

  [Test]
  [Category("Unit")]
  public void APlaneCountNoScreenUsesIsRefused() {
    var data = _Build(320, 200, 4);
    data[13] = 9;

    Assert.Throws<System.IO.InvalidDataException>(() => PrismPaintReader.FromBytes(data));
  }
}
