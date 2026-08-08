using System;
using FileFormat.Core;

namespace FileFormat.Vue.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Indexed(int width, int height) {
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 13);

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
    var decoded = VueFile.ToRawImage(VueReader.FromBytes(VueWriter.ToBytes(VueFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(
        PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData,
        Is.EqualTo(PixelConverter.Convert(source, PixelFormat.Rgb24).PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = VueFile.ToRawImage(VueReader.FromBytes(VueWriter.ToBytes(VueFile.FromRawImage(_Indexed(200, 3)))));
    var tall = VueFile.ToRawImage(VueReader.FromBytes(VueWriter.ToBytes(VueFile.FromRawImage(_Indexed(3, 200)))));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 5, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[20] };
    var decoded = VueFile.ToRawImage(VueReader.FromBytes(VueWriter.ToBytes(VueFile.FromRawImage(grey))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((5, 4)));
  }

  /// <summary>
  /// Nothing states where the picture is. Following the two lengths has to land exactly on the GIF's
  /// own signature, and the size stated in front of it has to be the size the GIF states for itself —
  /// which is what says the fields are being written as the format means them.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheLengthsLandOnThePicture() {
    var file = VueFile.FromRawImage(_Indexed(37, 11));
    var bytes = VueWriter.ToBytes(file);

    var at = 30;
    for (var i = 0; i < 2; ++i)
      at += 2 + (bytes[at] | (bytes[at + 1] << 8));

    at += 8;

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, VueFile.Magic.Length).SequenceEqual(VueFile.Magic), Is.True);
      Assert.That(bytes.AsSpan(at, 4).ToArray(), Is.EqualTo(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }));
      Assert.That((file.Width, file.Height), Is.EqualTo((37, 11)));
    });
  }
}
