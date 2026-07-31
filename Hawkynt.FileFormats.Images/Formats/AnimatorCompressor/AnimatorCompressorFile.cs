using System;
using FileFormat.Core;

namespace FileFormat.AnimatorCompressor;

/// <summary>In-memory representation of a Kompresor do Animatora sheet (.kpr).</summary>
/// <remarks>
/// An animation stored as tiles and a map, which is the compression the name refers to: consecutive
/// frames of an animation mostly repeat each other, so naming an eight-by-eight tile costs a byte
/// where drawing it costs eight. Only the tiles a frame actually uses are stored, once each.
/// <para/>
/// The frames are laid out side by side rather than one after another, so a row of the sheet runs
/// across every frame at that height — which is what makes an animation readable as a single
/// picture.
/// </remarks>
public readonly record struct AnimatorCompressorFile
  : IImageFormatReader<AnimatorCompressorFile>, IImageToRawImage<AnimatorCompressorFile> {

  /// <summary>Offset of the tile map, past the executable header and the three counts.</summary>
  public const int MapOffset = 11;

  /// <summary>Bytes a tile occupies: eight rows of four two-bit pixels.</summary>
  public const int TileLength = 8;

  static string IImageFormatMetadata<AnimatorCompressorFile>.PrimaryExtension => ".kpr";
  static string[] IImageFormatMetadata<AnimatorCompressorFile>.FileExtensions => [".kpr"];
  static AnimatorCompressorFile IImageFormatReader<AnimatorCompressorFile>.FromSpan(ReadOnlySpan<byte> data)
    => AnimatorCompressorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AnimatorCompressorFile>.VideoModes => [
    new("Animation", [(IntegerRange.Any, IntegerRange.Any)], [4])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Frames the animation holds.</summary>
  public int Frames { get; init; }

  /// <summary>Tiles across one frame.</summary>
  public int Columns { get; init; }

  /// <summary>Tiles down.</summary>
  public int Rows { get; init; }

  /// <summary>Entries the tile map holds.</summary>
  public int MapLength => Frames * Columns * Rows;

  public static RawImage ToRawImage(AnimatorCompressorFile file) {
    var data = file.Data ?? [];
    var frameWidth = file.Columns << 3;
    var width = file.Frames * frameWidth;
    var height = file.Rows << 3;
    var tiles = MapOffset + file.MapLength;
    var frame = new byte[width * height];

    var target = 0;
    for (var y = 0; y < height; ++y)
    for (var f = 0; f < file.Frames; ++f)
    for (var x = 0; x < frameWidth; ++x) {
      var tile = data[MapOffset + (f * file.Rows + (y >> 3)) * file.Columns + (x >> 3)];
      var at = tiles + tile * TileLength + (y & 7);
      var pixel = at < data.Length ? (data[at] >> (~x & 6)) & 3 : 0;

      // The four values are luminances of one hue, four steps apart.
      frame[target++] = (byte)(pixel << 2);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
