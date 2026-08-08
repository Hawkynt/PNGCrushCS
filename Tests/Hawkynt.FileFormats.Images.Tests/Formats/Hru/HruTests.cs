using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Gif;
using FileFormat.Hru;

namespace FileFormat.Hru.Tests;

[TestFixture]
public sealed class HruTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 7);

    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 3);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 256,
    };
  }

  /// <summary>The same picture as an HRU: a GIF with its signature swapped for the fixed run.</summary>
  /// <remarks>
  /// Everything from the logical screen descriptor onward is copied across unchanged, which is what
  /// the format is — so a GIF this library wrote is the shortest way to a valid one.
  /// </remarks>
  private static byte[] _File(RawImage image) {
    var gif = GifWriter.ToBytes(GifFile.FromRawImage(image));

    // Screen descriptor, then the global table its flags ask for, then whatever follows.
    var flags = gif[6 + 4];
    var paletteBytes = (flags & 0x80) != 0 ? 3 * (1 << ((flags & 7) + 1)) : 0;
    var at = 6 + HruFile.ScreenDescriptorSize + paletteBytes;

    // Skip any extension blocks so the image descriptor lands where HRU keeps its ten bytes.
    while (gif[at] == 0x21) {
      at += 2;
      while (gif[at] != 0)
        at += gif[at] + 1;
      ++at;
    }

    var body = gif.AsSpan(6 + HruFile.ScreenDescriptorSize + paletteBytes);
    var descriptorAt = at - (6 + HruFile.ScreenDescriptorSize + paletteBytes);

    using var ms = new MemoryStream();
    ms.Write(HruFile.Magic);
    ms.Write(gif.AsSpan(6, HruFile.ScreenDescriptorSize));
    ms.Write(gif.AsSpan(6 + HruFile.ScreenDescriptorSize, paletteBytes));
    ms.Write(body[descriptorAt..(descriptorAt + HruFile.ImageDescriptorSize)]);
    ms.Write(body[(descriptorAt + HruFile.ImageDescriptorSize)..]);

    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HruReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HruReader.FromBytes(new byte[128]));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoGlobalColourTable_ThrowsInvalidDataException() {
    var data = _File(_Picture(8, 4));
    data[HruFile.MagicSize + 4] &= 0x7F;

    Assert.Throws<InvalidDataException>(() => HruReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedAfterTheHeader_ThrowsInvalidDataException() {
    var data = _File(_Picture(8, 4));
    Array.Resize(ref data, HruFile.MagicSize + HruFile.ScreenDescriptorSize + 4);

    Assert.Throws<InvalidDataException>(() => HruReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ThePictureComesBackAtItsSizeAndColours() {
    var original = _Picture(24, 16);
    var decoded = HruFile.ToRawImage(HruReader.FromBytes(_File(original)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(24));
      Assert.That(decoded.Height, Is.EqualTo(16));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.PixelData, Is.EqualTo(original.PixelData), "the coded data unpacks to the picture it went in as");
    });
  }
}
