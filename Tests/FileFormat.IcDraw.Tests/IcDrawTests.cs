using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.IcDraw;

namespace FileFormat.IcDraw.Tests;

[TestFixture]
public sealed class IcDrawTests {

  private static RawImage _Icon(byte alpha = 255) {
    const int size = IcDrawFile.IconSize;
    var data = new byte[size * size * 4];
    for (var y = 0; y < size; ++y)
    for (var x = 0; x < size; ++x) {
      var o = (y * size + x) * 4;
      data[o] = (byte)(x < size / 2 ? 255 : 0);
      data[o + 1] = (byte)(y < size / 2 ? 255 : 0);
      data[o + 2] = 0;
      data[o + 3] = x < size / 2 ? (byte)255 : alpha;
    }

    return new() { Width = size, Height = size, Format = PixelFormat.Bgra32, PixelData = data };
  }

  private static IcDrawFile _Group() => new() {
    Variant = IcDrawVariant.IconGroup,
    Header = new byte[IcDrawFile.HeaderSize],
    ImageData = new byte[IcDrawFile.ImageDataSize],
    Mask = [],
    AdditionalImages = new byte[IcDrawFile.ImageDataSize * 2],
  };

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesASingleIconFile() {
    var bytes = IcDrawWriter.ToBytes(IcDrawFile.FromRawImage(_Icon()));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(704));
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("ICBI"));
      Assert.That(bytes[IcDrawFile.SizeOffset + 1], Is.EqualTo(IcDrawFile.IconSize));
      Assert.That(bytes[IcDrawFile.SizeOffset + 3], Is.EqualTo(IcDrawFile.IconSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_MarksOpaquePixelsInTheMask() {
    // The right half is fully transparent, so its mask bits must be clear.
    var file = IcDrawFile.FromRawImage(_Icon(alpha: 0));
    const int size = IcDrawFile.IconSize;

    Assert.Multiple(() => {
      Assert.That(file.Mask[0], Is.EqualTo(0xFF), "the left half is opaque");
      Assert.That(file.Mask[size / 8 - 1], Is.Zero, "the right half is not");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_MapsIntoTheFixedPalette() {
    var raw = IcDrawFile.ToRawImage(IcDrawFile.FromRawImage(_Icon()));

    Assert.Multiple(() => {
      Assert.That(raw.PaletteCount, Is.EqualTo(IcDrawFile.ColorCount));
      Assert.That(raw.Palette, Is.EqualTo(IcDrawFile.Palette.ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesASingleIcon() {
    var file = IcDrawFile.FromRawImage(_Icon(alpha: 0));
    var restored = IcDrawReader.FromBytes(IcDrawWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Variant, Is.EqualTo(IcDrawVariant.SingleIcon));
      Assert.That(restored.ImageData, Is.EqualTo(file.ImageData));
      Assert.That(restored.Mask, Is.EqualTo(file.Mask));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAnIconGroup() {
    var bytes = IcDrawWriter.ToBytes(_Group());
    var restored = IcDrawReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(1600));
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("ICB3"));
      Assert.That(restored.Variant, Is.EqualTo(IcDrawVariant.IconGroup));
      Assert.That(restored.AdditionalImages, Has.Length.EqualTo(IcDrawFile.ImageDataSize * 2));
      Assert.That(restored.Mask, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnUnknownSize()
    => Assert.Throws<InvalidDataException>(() => IcDrawReader.FromBytes(new byte[1024]));

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAGroupTagOnASingleIconFile() {
    var bytes = IcDrawWriter.ToBytes(IcDrawFile.FromRawImage(_Icon()));
    IcDrawFile.IconGroupSignature.CopyTo(bytes);

    Assert.Throws<InvalidDataException>(() => IcDrawReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 16, Height = 16, Format = PixelFormat.Bgra32, PixelData = new byte[16 * 16 * 4] };

    Assert.Throws<ArgumentException>(() => IcDrawFile.FromRawImage(raw));
  }
}
