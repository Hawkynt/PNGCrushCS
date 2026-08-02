using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen5;

/// <summary>In-memory representation of an MSX2 Screen 5 image.
/// Layout: optional 7-byte BSAVE header (0xFE magic) + pixel data (27136 bytes, 4bpp) + optional palette (32 bytes).
/// 256x212, 16 colors, 2 pixels per byte (high nibble = left, low nibble = right).
/// </summary>
// The byte 0xFE opens every BSAVE file the MSX writes, whichever screen mode it holds, so it says
// what the container is and nothing about which of these formats this is. Nine of them declared it
// as their magic, and the registry consults magic before extension — so whichever it happened to
// reach first took every MSX picture. A Screen 5 file, 256 by 212, was being opened as a Screen 6
// one and drawn 512 by 424. The extension is what tells these apart, and it is what decides now.
public readonly record struct MsxScreen5File : IImageFormatReader<MsxScreen5File>, IImageToRawImage<MsxScreen5File>, IImageFormatWriter<MsxScreen5File> {

  static string IImageFormatMetadata<MsxScreen5File>.PrimaryExtension => ".sc5";
  static string[] IImageFormatMetadata<MsxScreen5File>.FileExtensions => [".sc5", ".ge5"];
  static MsxScreen5File IImageFormatReader<MsxScreen5File>.FromSpan(ReadOnlySpan<byte> data) => MsxScreen5Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxScreen5File>.ToBytes(MsxScreen5File file) => MsxScreen5Writer.ToBytes(file);

  /// <summary>Fixed width of an MSX Screen 5 image.</summary>
  public const int FixedWidth = 256;

  /// <summary>Fixed height of an MSX Screen 5 image.</summary>
  public const int FixedHeight = 212;

  /// <summary>BSAVE header magic byte.</summary>
  public const byte BsaveMagic = 0xFE;

  /// <summary>BSAVE header size in bytes.</summary>
  public const int BsaveHeaderSize = 7;

  /// <summary>Pixel data size in bytes (256x212 / 2).</summary>
  public const int PixelDataSize = 27136;

  /// <summary>MSX2 palette size in bytes (16 entries x 2 bytes).</summary>
  public const int PaletteSize = 32;

  /// <summary>Raw pixel data size plus palette.</summary>
  public const int FullDataSize = PixelDataSize + PaletteSize;

  /// <summary>Default MSX2 V9938 palette as 32 bytes (16 entries x 2 bytes: 0RRR0BBB 00000GGG).</summary>
  internal static readonly byte[] DefaultMsx2Palette = [
    0x00, 0x00, // 0: transparent/black
    0x00, 0x00, // 1: black
    0x11, 0x06, // 2: medium green
    0x33, 0x07, // 3: light green
    0x17, 0x01, // 4: dark blue
    0x27, 0x03, // 5: light blue
    0x51, 0x01, // 6: dark red
    0x27, 0x06, // 7: cyan
    0x71, 0x01, // 8: medium red
    0x73, 0x03, // 9: light red
    0x61, 0x06, // 10: dark yellow
    0x64, 0x06, // 11: light yellow
    0x11, 0x04, // 12: dark green
    0x65, 0x02, // 13: magenta
    0x55, 0x05, // 14: gray
    0x77, 0x07, // 15: white
  ];

  /// <summary>Image width, always 256.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 212.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw pixel data (27136 bytes, 2 pixels per byte, high nibble = left pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>MSX2 V9938 palette (32 bytes: 16 entries x 2 bytes), or null if no palette in file.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Whether the original data had a 7-byte BSAVE header.</summary>
  public bool HasBsaveHeader { get; init; }

  /// <summary>Converts this MSX Screen 5 image to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(MsxScreen5File file) {

    var pixels = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var byteX = 0; byteX < 128; ++byteX) {
        var srcOffset = y * 128 + byteX;
        if (srcOffset >= file.PixelData.Length)
          break;

        var b = file.PixelData[srcOffset];
        var x = byteX * 2;
        pixels[y * FixedWidth + x] = (byte)((b >> 4) & 0x0F);
        if (x + 1 < FixedWidth)
          pixels[y * FixedWidth + x + 1] = (byte)(b & 0x0F);
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      // A file need not carry a palette, and many do not; the machine's own is what it would have
      // been drawn with. Handing back none at all left the picture with no colours to be drawn in,
      // which is not a decode.
      Palette = _ConvertMsx2Palette(file.Palette) ?? _ConvertMsx2Palette(DefaultMsx2Palette),
      PaletteCount = 16,
    };
  }

  /// <summary>Converts MSX V9938 palette (16 entries x 2 bytes: 0RRR0BBB, 00000GGG) to RGB triplets.</summary>
  /// <summary>
  /// Turns the machine's palette into whole bytes a channel.
  /// </summary>
  /// <remarks>
  /// This was a second copy of what <see cref="MsxGraphics.PaletteToRgb"/> already does, and the two
  /// widened a three-bit channel differently: dividing by seven and truncating gives 145 for a value
  /// of four where repeating the bits gives 146. One step in 255 is invisible, but it is the whole
  /// of the difference between this and every other decoder, and there is no reason to keep two
  /// answers to the same question.
  /// </remarks>
  internal static byte[]? _ConvertMsx2Palette(byte[]? msxPalette)
    => msxPalette == null || msxPalette.Length < PaletteSize
      ? null
      : MsxGraphics.PaletteToRgb(msxPalette, 16);
}
