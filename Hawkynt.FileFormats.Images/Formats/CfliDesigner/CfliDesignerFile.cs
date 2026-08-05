using System;
using FileFormat.Core;

namespace FileFormat.CfliDesigner;

/// <summary>In-memory representation of a CFLI Designer picture for the Commodore 64.</summary>
/// <remarks>
/// This wanted 17002 bytes read as an ordinary multicolour FLI — a bitmap, eight video matrices and
/// colour memory. A CFLI is 8170 bytes and every sample is exactly that, so all of them were refused
/// for being under half the length demanded, and the fixed length is itself the tell: a file whose
/// size does not move with the picture is not a packed one.
/// <para/>
/// It holds the eight matrices and nothing else. There is no bitmap in the file because the format
/// does not vary one: the C in CFLI is colour, and the editor paints only the two colours a cell row
/// shows while the hardware runs a fixed alternating pattern behind them. So a pixel takes the
/// foreground nibble in the even columns of a cell and the background nibble in the odd ones, which
/// draws the two as a fine vertical stripe that at the machine's dot pitch reads as a blend.
/// <para/>
/// Established against all three distinct samples: reading it this way, every index maps to exactly
/// one of the colours RECOIL draws and back, in twelve, thirteen and sixteen colours respectively.
/// Reading the foreground nibble everywhere instead is right in 92 to 98 percent of pixels, and the
/// pixels it is wrong at are the odd columns and nothing else — which is what identified the
/// pattern.
/// </remarks>
public readonly record struct CfliDesignerFile : IImageFormatReader<CfliDesignerFile>, IImageToRawImage<CfliDesignerFile>, IImageFormatWriter<CfliDesignerFile> {

  static string IImageFormatMetadata<CfliDesignerFile>.PrimaryExtension => ".cfli";
  static string[] IImageFormatMetadata<CfliDesignerFile>.FileExtensions => [".cfli"];
  static CfliDesignerFile IImageFormatReader<CfliDesignerFile>.FromSpan(ReadOnlySpan<byte> data) => CfliDesignerReader.FromSpan(data);
  static byte[] IImageFormatWriter<CfliDesignerFile>.ToBytes(CfliDesignerFile file) => CfliDesignerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CfliDesignerFile>.VideoModes => [
    new("CFLI", [(VisibleWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The pixels a row holds, the first three cells of which are not part of the picture.</summary>
  public const int FixedWidth = 320;

  /// <summary>Pixels across the picture: FLI cannot colour the first 24 of a row in time.</summary>
  public const int VisibleWidth = 296;

  /// <summary>Where the picture starts within a row.</summary>
  internal const int HiddenColumns = FixedWidth - VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = 200;

  /// <summary>Character columns held in memory.</summary>
  internal const int Columns = FixedWidth / 8;

  internal const int LoadAddressSize = 2;

  /// <summary>How many video matrices, one for each raster line of a character cell.</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>The entries a matrix holds.</summary>
  internal const int ScreenBankSize = 1000;

  /// <summary>The address space a matrix occupies: a whole page for the thousand it uses.</summary>
  internal const int ScreenBankStride = 1024;

  /// <summary>
  /// The whole of a file. The last matrix is not padded, which is where the 24 comes from.
  /// </summary>
  public const int ExpectedFileSize =
    LoadAddressSize + (ScreenBankCount - 1) * ScreenBankStride + ScreenBankSize;

  /// <summary>Always 296.</summary>
  public int Width => VisibleWidth;

  /// <summary>Always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The eight video matrices, one after another, a thousand entries apiece.</summary>
  public byte[] Screens { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(CfliDesignerFile file) {
    var screens = file.Screens ?? [];
    var indices = new byte[VisibleWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < VisibleWidth; ++x) {
        var column = x + HiddenColumns;
        var cell = y / 8 * Columns + column / 8;

        // Which of the eight matrices speaks for this row is what makes it FLI; which nibble of its
        // entry speaks for this column is the fixed pattern the format never stores.
        var entry = screens[y % ScreenBankCount * ScreenBankSize + cell];
        indices[y * VisibleWidth + x] = (byte)(column % 2 == 0 ? entry >> 4 : entry & 0x0F);
      }

    return new() {
      Width = VisibleWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }
}
