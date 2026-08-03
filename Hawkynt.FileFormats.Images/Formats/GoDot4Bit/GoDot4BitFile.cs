using System;
using FileFormat.Core;

namespace FileFormat.GoDot4Bit;

/// <summary>In-memory representation of a GoDot picture or clip for the Commodore 64.</summary>
/// <remarks>
/// This used to expect exactly 16384 raw bytes at a fixed 160 by 200 and no signature at all. No
/// GoDot file is anything like that: all six in the corpus open with "GOD0" or "GOD1", are packed,
/// and none is 160 wide. Not one of them was read while RECOIL draws them.
/// <para/>
/// A "GOD0" picture is a whole 320 by 200 screen. A "GOD1" is a clip, and states its own size in
/// character cells — two bytes of where it was cut from, then the width and the height. Both are
/// packed the same way and hold the same four bits a pixel, a character cell at a time.
/// </remarks>
public readonly record struct GoDot4BitFile
  : IImageFormatReader<GoDot4BitFile>, IImageToRawImage<GoDot4BitFile>, IImageFormatWriter<GoDot4BitFile> {

  static string IImageFormatMetadata<GoDot4BitFile>.PrimaryExtension => ".4bt";
  static string[] IImageFormatMetadata<GoDot4BitFile>.FileExtensions => [".4bt", ".4bit", ".clp"];
  static GoDot4BitFile IImageFormatReader<GoDot4BitFile>.FromSpan(ReadOnlySpan<byte> data) => GoDot4BitReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GoDot4BitFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16])
  ];
  static byte[] IImageFormatWriter<GoDot4BitFile>.ToBytes(GoDot4BitFile file) => GoDot4BitWriter.ToBytes(file);

  /// <summary>.clp belongs to two other formats as well, so the signature is what settles it.</summary>
  static bool? IImageFormatMetadata<GoDot4BitFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[..3].SequenceEqual("GOD"u8) && header[3] is (byte)'0' or (byte)'1'
      ? true
      : null;

  /// <summary>The four bytes a whole screen opens with.</summary>
  internal static ReadOnlySpan<byte> ScreenMagic => "GOD0"u8;

  /// <summary>The four bytes a clip opens with, its size following.</summary>
  internal static ReadOnlySpan<byte> ClipMagic => "GOD1"u8;

  /// <summary>The byte that introduces a run: a count and then the value.</summary>
  internal const byte RunEscape = 0xAD;

  /// <summary>A whole screen is this wide.</summary>
  internal const int ScreenWidth = 320;

  /// <summary>A whole screen is this tall.</summary>
  internal const int ScreenHeight = 200;

  /// <summary>Bytes one character cell takes: eight rows of four, two pixels to a byte.</summary>
  internal const int CellSize = 32;

  /// <summary>
  /// The Commodore colours in the order GoDot numbers them, which is by brightness rather than by
  /// the machine's own numbering.
  /// </summary>
  /// <remarks>
  /// Ten of the sixteen are settled by the samples: nought is black, two is brown, four red, six
  /// orange, seven the middle grey, nine light red, eleven light grey, twelve cyan, thirteen yellow
  /// and fifteen white. The other six do not appear in any of them and follow the same brightness
  /// ordering, which is where thirteen and fourteen would otherwise be interchangeable — the samples
  /// put yellow at thirteen.
  /// </remarks>
  internal static ReadOnlySpan<int> BrightnessOrder => [0, 6, 9, 11, 2, 4, 8, 12, 5, 10, 14, 15, 3, 7, 13, 1];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Whether this is a clip rather than a whole screen.</summary>
  public bool IsClip { get; init; }

  /// <summary>Unpacked pixels: four bits each, a character cell at a time, high nibble first.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this GoDot picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(GoDot4BitFile file) {
    var width = file.Width;
    var height = file.Height;
    var cells = file.PixelData ?? [];
    var cellColumns = width / 8;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var offset = ((y / 8 * cellColumns) + x / 8) * CellSize + y % 8 * 4 + x % 8 / 2;
        var packed = offset < cells.Length ? cells[offset] : (byte)0;
        var value = (x & 1) == 0 ? packed >> 4 : packed & 0x0F;

        var color = Commodore64Graphics.HexColors[BrightnessOrder[value]];
        var at = (y * width + x) * 3;
        rgb[at] = (byte)(color >> 16);
        rgb[at + 1] = (byte)(color >> 8);
        rgb[at + 2] = (byte)color;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }
}
