using System;
using FileFormat.Core;

namespace FileFormat.TaquartInterlace;

/// <summary>In-memory representation of a Taquart Interlace Picture (.tip).</summary>
/// <remarks>
/// Three fields where the Atari's usual colour trick uses two. A Graphics 9 luminance field and a
/// Graphics 10 field of freely chosen registers are each combined with the same Graphics 11 hue
/// field on alternate scanlines, and the two resulting pictures are then averaged again by the
/// television. Sharing one hue field between them is what keeps the file to three equal parts.
/// <para/>
/// The picture is drawn at twice its stored size both ways, and the Graphics 10 field starts two
/// pixels later than the others — a consequence of when the chip fetches its first byte, which the
/// artist drew around rather than something to correct.
/// <para/>
/// The displacement is what makes reading one easy and drawing one hard. A displayed column takes
/// its luminance from one Graphics 9 nibble, its hue from a Graphics 11 nibble in step with it, and
/// its second luminance from a Graphics 10 nibble two pixels out of step — so within any four-pixel
/// group the two luminance fields disagree about where their group boundaries are for half of it.
/// Vertically the same: only the odd scanlines carry a stored luminance, and the even ones are the
/// mean of the two around them, so a row cannot be chosen without its neighbours.
/// <para/>
/// None of that makes a picture unfittable, though, because it is a chain rather than a tangle:
/// every Graphics 10 nibble joins exactly two luminance-and-hue nibbles, and every even row joins
/// exactly two odd ones. One pass along a scanline settles the whole of it, and rows settled in
/// order each know the one above — see <see cref="TaquartInterlaceEncoder"/>. What the format costs
/// a picture is its colours, not its detail: both fields draw one hue at a column, so a displayed
/// colour is the mean of two entries of one row of the palette and nothing else.
/// </remarks>
public readonly record struct TaquartInterlaceFile
  : IImageFormatReader<TaquartInterlaceFile>, IImageToRawImage<TaquartInterlaceFile>,
    IImageFromRawImage<TaquartInterlaceFile>, IImageFormatWriter<TaquartInterlaceFile> {

  /// <summary>The bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [(byte)'T', (byte)'I', (byte)'P', 1, 0];

  /// <summary>Offset of the first field.</summary>
  public const int FieldsOffset = 9;

  /// <summary>Widest picture the format can hold, in stored pixels.</summary>
  public const int MaxStoredWidth = 160;

  /// <summary>Tallest picture the format can hold, in stored rows.</summary>
  public const int MaxStoredHeight = 119;

  /// <summary>How far right the Graphics 10 field's own timing pushes it.</summary>
  public const int LeftSkip = 1;

  /// <summary>The nine colour registers all three fields draw from.</summary>
  public static ReadOnlySpan<byte> Registers => [0, 2, 4, 6, 8, 10, 12, 14, 0];

  static string IImageFormatMetadata<TaquartInterlaceFile>.PrimaryExtension => ".tip";
  static string[] IImageFormatMetadata<TaquartInterlaceFile>.FileExtensions => [".tip"];
  static TaquartInterlaceFile IImageFormatReader<TaquartInterlaceFile>.FromSpan(ReadOnlySpan<byte> data)
    => TaquartInterlaceReader.FromSpan(data);
  static byte[] IImageFormatWriter<TaquartInterlaceFile>.ToBytes(TaquartInterlaceFile file)
    => TaquartInterlaceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TaquartInterlaceFile>.VideoModes => [
    new("Taquart Interlace", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Stored pixels across; the picture is drawn at twice this.</summary>
  public int StoredWidth { get; init; }

  /// <summary>Stored rows; the picture is drawn at twice this.</summary>
  public int StoredHeight { get; init; }

  /// <summary>Bytes one field occupies.</summary>
  public int FieldLength { get; init; }

  public static RawImage ToRawImage(TaquartInterlaceFile file) {
    var data = file.Data ?? [];
    int width = file.StoredWidth * 2, height = file.StoredHeight * 2;
    var stride = file.StoredWidth >> 2;
    var entries = Atari8BitGraphics.ExpandGr10Registers(Registers);
    var hues = FieldsOffset + file.FieldLength * 2;

    var first = new byte[width * height];
    Atari8BitGraphics.DecodeGr9Into(
      data, FieldsOffset, stride, first, width, width * 2, width, file.StoredHeight, 0, LeftSkip);
    Atari8BitGraphics.BlendGr11Into(data, hues, stride, first, width, height, 0, LeftSkip);

    var second = new byte[width * height];
    Atari8BitGraphics.DecodeGr10Into(
      data, FieldsOffset + file.FieldLength, second, width, width * 2, width, file.StoredHeight, entries, LeftSkip);
    Atari8BitGraphics.BlendGr11Into(data, hues, stride, second, width, height, 0, LeftSkip);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(
        Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }

  /// <summary>Draws a picture as three interlaced fields.</summary>
  /// <remarks>
  /// The format holds any size up to its maximum, so a picture keeps as much of its own as the
  /// stored fields can state: half of it each way, the width rounded to the four stored pixels a
  /// nibble pair covers. What does not fit is sampled down rather than refused.
  /// </remarks>
  public static TaquartInterlaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var storedWidth = Math.Clamp((image.Width / 2 + 2) / 4 * 4, 4, MaxStoredWidth);
    var storedHeight = Math.Clamp((image.Height + 1) / 2, 1, MaxStoredHeight);

    return TaquartInterlaceReader.FromBytes(TaquartInterlaceEncoder.Encode(
      image.SampleTo(storedWidth * 2, storedHeight * 2).PixelData, storedWidth, storedHeight));
  }
}
