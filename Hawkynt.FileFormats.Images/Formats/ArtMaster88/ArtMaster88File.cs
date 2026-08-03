using System;
using FileFormat.Core;

namespace FileFormat.ArtMaster88;

/// <summary>In-memory representation of an Art Master 88 picture (.arv).</summary>
/// <remarks>
/// One format across two machines. The 200-line form is a PC-88 screen: three planes that are the
/// three channels directly, so it has eight colours and no palette to store. The 400-line form is a
/// PC-98 screen with three or four planes and a palette of four bits a channel — and the number of
/// planes is not stated but inferred from how long the palette is.
/// <para/>
/// Both are packed by the same scheme, which spends nothing at all on marking runs: a byte
/// repeating the one before it means a run follows, and the count comes next. A value therefore
/// costs two bytes to repeat twice and three to repeat any number of times, and a stream with no
/// repeats in it is the same size as the data.
/// </remarks>
public readonly record struct ArtMaster88File
  : IImageFormatReader<ArtMaster88File>, IImageToRawImage<ArtMaster88File>,
    IImageFromRawImage<ArtMaster88File>, IImageFormatWriter<ArtMaster88File> {

  /// <summary>Pixels across, in both forms.</summary>
  public const int Width = 640;

  /// <summary>Rows the picture is shown at, in both forms.</summary>
  public const int Height = 400;

  /// <summary>Bytes one plane row occupies.</summary>
  public const int BytesPerRow = Width / 8;

  static string IImageFormatMetadata<ArtMaster88File>.PrimaryExtension => ".arv";
  /// <summary>
  /// Also .img, which all three samples in the corpus carry.
  /// </summary>
  /// <remarks>
  /// Only .arv was claimed and no sample has it, so none of the three was read though this reader
  /// decodes every one of them exactly as RECOIL does. The extension is shared with several other
  /// formats, so the "SS_SIF" a real file opens with is stated below to settle it on content.
  /// </remarks>
  static string[] IImageFormatMetadata<ArtMaster88File>.FileExtensions => [".arv", ".img"];

  static bool? IImageFormatMetadata<ArtMaster88File>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 6 && header[..6].SequenceEqual("SS_SIF"u8) ? true : null;
  static ArtMaster88File IImageFormatReader<ArtMaster88File>.FromSpan(ReadOnlySpan<byte> data)
    => ArtMaster88Reader.FromSpan(data);
  static byte[] IImageFormatWriter<ArtMaster88File>.ToBytes(ArtMaster88File file) => ArtMaster88Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ArtMaster88File>.VideoModes => [
    new("PC-88", [(Width, Height)], [8]),
    new("PC-98", [(Width, Height)], [16]),
  ];

  /// <summary>Stored rows: 200 for the PC-88 form, each shown twice, and 400 for the PC-98 one.</summary>
  public int StoredHeight { get; init; }

  /// <summary>The unpacked planes.</summary>
  public byte[][] Planes { get; init; }

  /// <summary>Sixteen RGB triplets, or empty for the form whose planes are the channels.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(ArtMaster88File file) {
    var planes = file.Planes ?? [];
    var palette = file.Palette ?? [];
    var rgb = new byte[Width * Height * 3];
    var doubled = Height / file.StoredHeight;

    for (var y = 0; y < file.StoredHeight; ++y)
    for (var column = 0; column < BytesPerRow; ++column) {
      var offset = y * BytesPerRow + column;

      for (var x = 0; x < 8; ++x) {
        int red, green, blue;

        if (palette.Length == 0) {
          // The three planes are the three channels, in the order green, blue, red.
          red = ((planes[1][offset] >> (7 - x)) & 1) * 255;
          green = ((planes[2][offset] >> (7 - x)) & 1) * 255;
          blue = ((planes[0][offset] >> (7 - x)) & 1) * 255;
        } else {
          var index = 0;
          for (var plane = 0; plane < planes.Length; ++plane)
            index |= ((planes[plane][offset] >> (7 - x)) & 1) << plane;

          red = palette[index * 3];
          green = palette[index * 3 + 1];
          blue = palette[index * 3 + 2];
        }

        for (var repeat = 0; repeat < doubled; ++repeat) {
          var target = ((y * doubled + repeat) * Width + (column << 3) + x) * 3;
          rgb[target] = (byte)red;
          rgb[target + 1] = (byte)green;
          rgb[target + 2] = (byte)blue;
        }
      }
    }

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Builds a picture from an image, scaled to the machine's fixed screen.</summary>
  /// <remarks>
  /// The PC-88 form has no size field and no palette: 640 by 400 with one bit a channel, so the
  /// eight corners of the colour cube are all it holds. Its two hundred stored rows are each shown
  /// twice, which is why only every other row of the picture is kept.
  /// </remarks>
  public static ArtMaster88File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var planes = new byte[3][];
    for (var i = 0; i < 3; ++i)
      planes[i] = new byte[200 * BytesPerRow];

    for (var y = 0; y < 200; ++y)
    for (var x = 0; x < Width; ++x) {
      var sourceX = image.Width == Width ? x : x * image.Width / Width;
      var sourceY = image.Height == Height ? y * 2 : y * 2 * image.Height / Height;
      var source = (sourceY * image.Width + sourceX) * 3;

      var at = y * BytesPerRow + (x >> 3);
      var bit = (byte)(1 << (7 - (x & 7)));

      // The planes are the channels, in the order green, blue, red — which is why the picture's
      // own order has to be permuted rather than copied.
      if (rgb.PixelData[source] >= 128)
        planes[1][at] |= bit;

      if (rgb.PixelData[source + 1] >= 128)
        planes[2][at] |= bit;

      if (rgb.PixelData[source + 2] >= 128)
        planes[0][at] |= bit;
    }

    return new() { StoredHeight = 200, Planes = planes, Palette = [] };
  }
}
