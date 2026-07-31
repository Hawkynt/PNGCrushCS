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
  : IImageFormatReader<ArtMaster88File>, IImageToRawImage<ArtMaster88File> {

  /// <summary>Pixels across, in both forms.</summary>
  public const int Width = 640;

  /// <summary>Rows the picture is shown at, in both forms.</summary>
  public const int Height = 400;

  /// <summary>Bytes one plane row occupies.</summary>
  public const int BytesPerRow = Width / 8;

  static string IImageFormatMetadata<ArtMaster88File>.PrimaryExtension => ".arv";
  static string[] IImageFormatMetadata<ArtMaster88File>.FileExtensions => [".arv"];
  static ArtMaster88File IImageFormatReader<ArtMaster88File>.FromSpan(ReadOnlySpan<byte> data)
    => ArtMaster88Reader.FromSpan(data);
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
}
