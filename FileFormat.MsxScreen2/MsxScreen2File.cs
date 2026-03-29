using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxScreen2;

/// <summary>In-memory representation of an MSX Screen 2 (TMS9918) image.
/// Layout: optional 7-byte BSAVE header (0xFE magic) + VRAM data.
/// VRAM: Pattern generator table (6144 bytes) + Color table (6144 bytes) + Pattern name table (768 bytes) = 13056 bytes.
/// 256x192, pattern-based 16 colors (TMS9918 palette).
/// </summary>
[FormatMagicBytes([0xFE])]
public sealed class MsxScreen2File : IImageFileFormat<MsxScreen2File> {

  static string IImageFileFormat<MsxScreen2File>.PrimaryExtension => ".sc2";
  static string[] IImageFileFormat<MsxScreen2File>.FileExtensions => [".sc2", ".grp"];
  static MsxScreen2File IImageFileFormat<MsxScreen2File>.FromFile(FileInfo file) => MsxScreen2Reader.FromFile(file);
  static MsxScreen2File IImageFileFormat<MsxScreen2File>.FromBytes(byte[] data) => MsxScreen2Reader.FromBytes(data);
  static MsxScreen2File IImageFileFormat<MsxScreen2File>.FromStream(Stream stream) => MsxScreen2Reader.FromStream(stream);
  static byte[] IImageFileFormat<MsxScreen2File>.ToBytes(MsxScreen2File file) => MsxScreen2Writer.ToBytes(file);

  /// <summary>Fixed width of an MSX Screen 2 image.</summary>
  public const int FixedWidth = 256;

  /// <summary>Fixed height of an MSX Screen 2 image.</summary>
  public const int FixedHeight = 192;

  /// <summary>BSAVE header magic byte.</summary>
  public const byte BsaveMagic = 0xFE;

  /// <summary>BSAVE header size in bytes.</summary>
  public const int BsaveHeaderSize = 7;

  /// <summary>Size of the pattern generator table in bytes (3 banks x 2048).</summary>
  internal const int PatternGeneratorSize = 6144;

  /// <summary>Size of the color table in bytes (3 banks x 2048).</summary>
  internal const int ColorTableSize = 6144;

  /// <summary>Size of the pattern name table in bytes (32x24).</summary>
  internal const int PatternNameTableSize = 768;

  /// <summary>Total raw VRAM data size.</summary>
  public const int VramDataSize = PatternGeneratorSize + ColorTableSize + PatternNameTableSize;

  /// <summary>Total file size with BSAVE header.</summary>
  public const int FileWithHeaderSize = BsaveHeaderSize + VramDataSize;

  /// <summary>TMS9918 fixed 16-color palette as RGB triplets.</summary>
  internal static readonly byte[] Tms9918Palette = [
    0x00, 0x00, 0x00, // 0: transparent/black
    0x00, 0x00, 0x00, // 1: black
    0x21, 0xC8, 0x42, // 2: medium green
    0x5E, 0xDC, 0x78, // 3: light green
    0x54, 0x55, 0xED, // 4: dark blue
    0x7D, 0x76, 0xFC, // 5: light blue
    0xD4, 0x52, 0x4D, // 6: dark red
    0x42, 0xEB, 0xF5, // 7: cyan
    0xFC, 0x55, 0x54, // 8: medium red
    0xFF, 0x79, 0x78, // 9: light red
    0xD4, 0xC1, 0x54, // 10: dark yellow
    0xE6, 0xCE, 0x80, // 11: light yellow
    0x21, 0xB0, 0x3B, // 12: dark green
    0xC9, 0x5B, 0xBA, // 13: magenta
    0xCC, 0xCC, 0xCC, // 14: gray
    0xFF, 0xFF, 0xFF, // 15: white
  ];

  /// <summary>Image width, always 256.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>Pattern generator table (6144 bytes: 3 banks x 256 patterns x 8 bytes).</summary>
  public byte[] PatternGenerator { get; init; } = [];

  /// <summary>Color table (6144 bytes: 3 banks x 256 patterns x 8 bytes, high nib = fg, low nib = bg per row).</summary>
  public byte[] ColorTable { get; init; } = [];

  /// <summary>Pattern name table (768 bytes: 32x24 cell indices).</summary>
  public byte[] PatternNameTable { get; init; } = [];

  /// <summary>Whether the original data had a 7-byte BSAVE header.</summary>
  public bool HasBsaveHeader { get; init; }

  /// <summary>Converts this MSX Screen 2 image to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(MsxScreen2File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = new byte[FixedWidth * FixedHeight];

    for (var charRow = 0; charRow < 24; ++charRow)
      for (var charCol = 0; charCol < 32; ++charCol) {
        var charIndex = file.PatternNameTable[charRow * 32 + charCol];
        var bank = charRow / 8;
        var patternOffset = bank * 2048 + charIndex * 8;
        var colorOffset = bank * 2048 + charIndex * 8;

        for (var pixelRow = 0; pixelRow < 8; ++pixelRow) {
          var patternByte = patternOffset + pixelRow < file.PatternGenerator.Length
            ? file.PatternGenerator[patternOffset + pixelRow]
            : (byte)0;
          var colorByte = colorOffset + pixelRow < file.ColorTable.Length
            ? file.ColorTable[colorOffset + pixelRow]
            : (byte)0;
          var foreground = (colorByte >> 4) & 0x0F;
          var background = colorByte & 0x0F;
          var y = charRow * 8 + pixelRow;

          for (var bit = 0; bit < 8; ++bit) {
            var x = charCol * 8 + bit;
            var isSet = ((patternByte >> (7 - bit)) & 1) != 0;
            pixels[y * FixedWidth + x] = (byte)(isSet ? foreground : background);
          }
        }
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = (byte[])Tms9918Palette.Clone(),
      PaletteCount = 16,
    };
  }

  /// <summary>Not supported. MSX Screen 2 has complex pattern-based constraints.</summary>
  public static MsxScreen2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to MsxScreen2File is not supported due to complex pattern-based constraints.");
  }
}
