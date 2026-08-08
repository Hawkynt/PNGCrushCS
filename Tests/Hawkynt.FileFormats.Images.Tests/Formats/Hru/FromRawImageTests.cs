using System;
using FileFormat.Core;
using FileFormat.Gif;

namespace FileFormat.Hru.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Indexed(int width, int height) {
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 17);

    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 3);
    }

    return new() {
      Width = width, Height = height, Format = PixelFormat.Indexed8,
      PixelData = pixels, Palette = palette, PaletteCount = 256,
    };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed_ReproducesEveryPixel() {
    var source = _Indexed(37, 11);
    var decoded = HruFile.ToRawImage(HruReader.FromBytes(HruWriter.ToBytes(HruFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(
        PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData,
        Is.EqualTo(PixelConverter.Convert(source, PixelFormat.Rgb24).PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = HruFile.FromRawImage(_Indexed(200, 3));
    var tall = HruFile.FromRawImage(_Indexed(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_ReducesATrueColourPictureToTheTableItCarries() {
    var data = new byte[37 * 11 * 3];
    for (var i = 0; i < 37 * 11; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    var file = HruFile.FromRawImage(new() { Width = 37, Height = 11, Format = PixelFormat.Rgb24, PixelData = data });
    var decoded = HruFile.ToRawImage(HruReader.FromBytes(HruWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.PaletteCount, Is.LessThanOrEqualTo(256));
    });
  }

  /// <summary>
  /// From the screen descriptor onward the file is a GIF, so everything the GIF writer emits after
  /// the signature must come across unchanged — and nothing may sit between the colour table and the
  /// coded data, which is where this format keeps the ten bytes that are not an image descriptor.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_IsAGifFromTheScreenDescriptorOnward() {
    var source = _Indexed(37, 11);
    var hru = HruWriter.ToBytes(HruFile.FromRawImage(source));
    var gif = GifWriter.ToBytes(GifFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(hru.AsSpan(0, HruFile.MagicSize).SequenceEqual(HruFile.Magic), Is.True);
      Assert.That(hru.Length, Is.EqualTo(HruFile.MagicSize + gif.Length - 6));
      Assert.That(
        hru.AsSpan(HruFile.MagicSize, HruFile.ScreenDescriptorSize).SequenceEqual(gif.AsSpan(6, HruFile.ScreenDescriptorSize)),
        Is.True, "the screen descriptor is the GIF's own");
    });
  }
}
