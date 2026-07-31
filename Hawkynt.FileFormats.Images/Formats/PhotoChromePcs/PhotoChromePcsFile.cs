using System;
using FileFormat.Core;

namespace FileFormat.PhotoChromePcs;

/// <summary>In-memory representation of a PhotoChrome compressed picture (.pcs).</summary>
/// <remarks>
/// An Atari ST picture that shows far more than sixteen colours by reloading the palette partway
/// across each scanline — not once but up to three times, at positions that depend on the pixel's
/// own colour index. The thresholds are not round numbers because they are cycle counts in
/// disguise: where the processor could get a write in between the video chip's fetches, and how
/// long that write took, depends on which register it was.
/// <para/>
/// A picture may also be two fields that alternate, in which case the second is stored as its
/// difference from the first — separately for the bitmap and for the palettes, either of which may
/// instead be stored outright.
/// </remarks>
public readonly record struct PhotoChromePcsFile
  : IImageFormatReader<PhotoChromePcsFile>, IImageToRawImage<PhotoChromePcsFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows. One short of a screen, the last being where the palettes run out.</summary>
  public const int Height = 199;

  /// <summary>Bytes one field's bitmap occupies.</summary>
  public const int BitmapSize = 32000;

  /// <summary>Bytes one plane occupies, the planes being stored as blocks rather than interleaved.</summary>
  public const int PlaneSize = 8000;

  /// <summary>Bytes one field occupies in all: the bitmap and then the palettes.</summary>
  public const int FieldSize = 51136;

  /// <summary>Bytes of palette a scanline owns, though it reads on into the next line's.</summary>
  public const int PaletteStride = 96;

  static string IImageFormatMetadata<PhotoChromePcsFile>.PrimaryExtension => ".pcs";
  static string[] IImageFormatMetadata<PhotoChromePcsFile>.FileExtensions => [".pcs"];
  static PhotoChromePcsFile IImageFormatReader<PhotoChromePcsFile>.FromSpan(ReadOnlySpan<byte> data)
    => PhotoChromePcsReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PhotoChromePcsFile>.VideoModes => [
    new("Atari ST", [(Width, Height)], [4096])
  ];

  /// <summary>The unpacked fields, one or two of them.</summary>
  public byte[][] Fields { get; init; }

  /// <summary>Whether the palettes are STE ones, decided from every field at once.</summary>
  public bool IsSte { get; init; }

  public static RawImage ToRawImage(PhotoChromePcsFile file) {
    var fields = file.Fields ?? [];
    var rendered = new byte[fields.Length][];

    for (var i = 0; i < fields.Length; ++i)
      rendered[i] = _DecodeField(fields[i], file.IsSte);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = rendered.Length == 1 ? rendered[0] : FrameBlend.Average(rendered[0], rendered[1]),
    };
  }

  private static byte[] _DecodeField(ReadOnlySpan<byte> field, bool ste) {
    var rgb = new byte[Width * Height * 3];
    var at = 0;

    for (var y = 0; y < Height; ++y) {
      // The bitmap starts one row in; the first stored row belongs to the line above the picture.
      var row = 40 + y * 40;

      for (var x = 0; x < Width; ++x) {
        // A palette entry is two bytes, so the index is doubled before anything is added to it.
        var entry = _PlanePixel(field, row + (x >> 3), x) << 1;

        // How many times the palette has been reloaded by the time the beam reaches this pixel.
        if (x >= entry * 2) {
          if (entry < 28) {
            if (x >= entry * 2 + 76) {
              if (x >= 176 + entry * 5 - (entry & 2) * 3)
                entry += 32;

              entry += 32;
            }
          } else if (x >= entry * 2 + 92)
            entry += 32;

          entry += 32;
        }

        // A line's fourth zone reads into the next line's palette, which is why the palette area
        // is longer than the number of lines times the stride.
        var color = AtariStGraphics.ColorAt(field, BitmapSize + y * PaletteStride + entry, ste);
        rgb[at++] = (byte)(color >> 16);
        rgb[at++] = (byte)(color >> 8);
        rgb[at++] = (byte)color;
      }
    }

    return rgb;
  }

  /// <summary>Reads one pixel from four planes stored as separate blocks.</summary>
  private static int _PlanePixel(ReadOnlySpan<byte> field, int offset, int x) {
    var bit = ~x & 7;
    var index = 0;

    for (var plane = 4; --plane >= 0;) {
      var at = offset + plane * PlaneSize;
      index = (index << 1) | (at >= 0 && at < field.Length ? (field[at] >> bit) & 1 : 0);
    }

    return index;
  }
}
