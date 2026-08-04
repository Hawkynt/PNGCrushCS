using System;
using FileFormat.Core;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

/// <summary>
/// Which byte of an uncompressed surface is which colour.
/// </summary>
/// <remarks>
/// The masks in a DDS header say where each channel's bits sit inside a little-endian word, so
/// A8R8G8B8 — the arrangement almost every file uses — puts blue in the lowest byte and lays the
/// pixel out in memory as blue, green, red, alpha. This was read as red-first regardless of the
/// masks, and written red-first under masks that said otherwise, so the reader and the writer here
/// agreed with each other and with nothing else. ImageMagick and XnView both read a file
/// ImageMagick had written as red where this project read blue.
/// </remarks>
[TestFixture]
public class DdsChannelOrderTests {

  /// <summary>The eight bytes of an uncompressed pure-red DDS, as ImageMagick writes one.</summary>
  private static byte[] _PureRed24Bit() {
    var data = new byte[128 + 3];

    // "DDS " and a header saying 1x1, twenty-four bits, with the ordinary masks.
    "DDS "u8.CopyTo(data);
    BitConverter.GetBytes(124).CopyTo(data, 4);                     // header size
    BitConverter.GetBytes(0x00001007).CopyTo(data, 8);              // caps, height, width, pixelformat
    BitConverter.GetBytes(1).CopyTo(data, 12);                      // height
    BitConverter.GetBytes(1).CopyTo(data, 16);                      // width
    // The pixel format sits seventy-two bytes into the header, which is seventy-six into the file.
    BitConverter.GetBytes(32).CopyTo(data, 76);                     // pixel format size
    BitConverter.GetBytes(0x40).CopyTo(data, 80);                   // DDPF_RGB
    BitConverter.GetBytes(24).CopyTo(data, 88);                     // bits per pixel
    BitConverter.GetBytes(0x00FF0000).CopyTo(data, 92);             // red
    BitConverter.GetBytes(0x0000FF00).CopyTo(data, 96);             // green
    BitConverter.GetBytes(0x000000FF).CopyTo(data, 100);            // blue

    // Red under those masks is blue nought, green nought, red full.
    data[128] = 0x00;
    data[129] = 0x00;
    data[130] = 0xFF;

    return data;
  }

  [Test]
  public void FromMasks_TheOrdinaryTwentyFourBitMasksAreBlueFirst() {
    var file = DdsReader.FromBytes(_PureRed24Bit());

    Assert.That(file.ChannelOrder, Is.EqualTo(DdsChannelOrder.Bgr));
  }

  [Test]
  public void ToRawImage_ReadsAPureRedFileAsRed() {
    // The whole point. Two independent tools read this file as red; before the masks were consulted
    // this project read it as blue.
    var rgb = DdsFile.ToRawImage(DdsReader.FromBytes(_PureRed24Bit())).ToRgb24();

    Assert.That(rgb[..3], Is.EqualTo(new byte[] { 255, 0, 0 }));
  }

  [Test]
  public void FromRawImage_WritesBlueFirstToMatchTheMasksItStates() {
    var red = new RawImage {
      Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [255, 0, 0],
    };

    var file = DdsFile.FromRawImage(red);

    Assert.Multiple(() => {
      Assert.That(file.ChannelOrder, Is.EqualTo(DdsChannelOrder.Bgr));
      Assert.That(file.Surfaces[0].Data, Is.EqualTo(new byte[] { 0, 0, 255 }));
    });
  }

  [Test]
  public void FromRawImage_APictureWithAlphaGoesOutAsThirtyTwoBitsBlueFirst() {
    var image = new RawImage {
      Width = 1, Height = 1, Format = PixelFormat.Rgba32, PixelData = [255, 0, 0, 128],
    };

    var file = DdsFile.FromRawImage(image);

    Assert.Multiple(() => {
      Assert.That(file.Format, Is.EqualTo(DdsFormat.Rgba));
      Assert.That(file.ChannelOrder, Is.EqualTo(DdsChannelOrder.Bgra));
      Assert.That(file.Surfaces[0].Data, Is.EqualTo(new byte[] { 0, 0, 255, 128 }));
    });
  }

  [Test]
  public void FromRawImage_AcceptsAPictureInAnyLayout() {
    // It used to refuse everything but five, which made the format unwritable for most of what a
    // caller might hand it.
    var indexed = new RawImage {
      Width = 2, Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 1],
      Palette = [255, 0, 0, 0, 0, 255],
      PaletteCount = 2,
    };

    var file = DdsFile.FromRawImage(indexed);

    Assert.That(file.Surfaces[0].Data, Is.EqualTo(new byte[] { 0, 0, 255, 255, 0, 0 }));
  }

  [Test]
  public void RoundTrip_APictureComesBackTheColourItWentIn() {
    var source = new RawImage {
      Width = 2, Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30],
    };

    var bytes = DdsWriter.ToBytes(DdsFile.FromRawImage(source));
    var restored = DdsFile.ToRawImage(DdsReader.FromBytes(bytes));

    Assert.That(restored.ToRgb24(), Is.EqualTo(source.PixelData));
  }
}
