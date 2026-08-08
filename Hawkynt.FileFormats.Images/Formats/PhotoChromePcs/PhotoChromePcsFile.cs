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
  : IImageFormatReader<PhotoChromePcsFile>, IImageToRawImage<PhotoChromePcsFile>,
    IImageFromRawImage<PhotoChromePcsFile>, IImageFormatWriter<PhotoChromePcsFile> {

  /// <summary>Colours one reload of the palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public const int Planes = 4;

  /// <summary>
  /// Times a scanline's palette is reloaded, each reload being another sixteen entries along.
  /// </summary>
  public const int Zones = 4;

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
  static byte[] IImageFormatWriter<PhotoChromePcsFile>.ToBytes(PhotoChromePcsFile file)
    => PhotoChromePcsWriter.ToBytes(file);
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

  /// <summary>
  /// Writes a picture as one field whose palette says the same thing wherever the beam reads it.
  /// </summary>
  /// <remarks>
  /// The palette is reloaded up to three times across a line, at thresholds that depend on the
  /// pixel's own colour index — and those thresholds are cycle counts in disguise rather than
  /// anything a file states. So the sixteen colours are written into all four of a line's reloads
  /// and into every line alike: whatever a decoder believes about where the reloads happen, it reads
  /// the same colour, and the picture is right on a tool that counts cycles differently from this
  /// one.
  /// <para/>
  /// That gives up what the format is for. Spending the reloads would want the colours of a line to
  /// depend on where along it they are used, and a line's fourth zone is physically the next line's
  /// first — the two share the same bytes — so the choice cannot be made a line at a time. Sixteen
  /// colours that are certainly right are worth more here than more that might not be.
  /// <para/>
  /// The palette is the plain ST form of three bits a channel, which is what keeps the reading
  /// unambiguous: the STE's fourth bit lives in bits the ST leaves clear, so a file is only taken
  /// for an STE one when some channel happens to be odd.
  /// </remarks>
  public static PhotoChromePcsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(Width, Height);
    var indexed = source.EnsureIndexedAtMost(ColorCount);
    var chosen = indexed.Palette ?? [];
    var field = new byte[FieldSize];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var index = indexed.PixelData[y * Width + x];

      // The first stored row belongs to the line above the picture, and the planes are blocks rather
      // than being interleaved.
      var at = 40 + y * 40 + (x >> 3);
      for (var plane = 0; plane < Planes; ++plane)
        if ((index & (1 << plane)) != 0)
          field[at + plane * PlaneSize] |= (byte)(1 << (~x & 7));
    }

    // A line's fourth zone reads into the next line's palette, so writing every line's sixteen
    // colours three times and one more set past the last line fills all four zones of every line.
    for (var y = 0; y <= Height; ++y)
    for (var zone = 0; zone < Zones - 1 && (y < Height || zone < 1); ++zone)
    for (var i = 0; i < ColorCount; ++i) {
      var word = (_ToThreeBits(chosen, i * 3) << 8)
                 | (_ToThreeBits(chosen, i * 3 + 1) << 4)
                 | _ToThreeBits(chosen, i * 3 + 2);

      var at = BitmapSize + y * PaletteStride + zone * ColorCount * 2 + i * 2;
      field[at] = (byte)(word >> 8);
      field[at + 1] = (byte)word;
    }

    return new() { Fields = [field], IsSte = false };
  }

  /// <summary>
  /// A channel on the eight levels the ST has, rounded rather than truncated so that what the reader
  /// expands back out is the level nearest what was asked for.
  /// </summary>
  private static int _ToThreeBits(ReadOnlySpan<byte> palette, int entry)
    => entry < palette.Length ? (palette[entry] * 7 + 127) / 255 : 0;

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
