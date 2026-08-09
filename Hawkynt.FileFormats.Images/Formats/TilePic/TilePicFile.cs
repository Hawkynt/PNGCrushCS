using System;
using FileFormat.Core;

namespace FileFormat.TilePic;

/// <summary>In-memory representation of a TilePic image (.tjp).</summary>
/// <remarks>
/// TilePic came out of the Berkeley Digital Library project as a way of keeping a large picture as a
/// pyramid of small tiles in one file instead of thousands of little ones, so that a viewer can
/// fetch the part of it that is on screen. Written from <em>tilepic(5)</em>, the format's own manual
/// page, which reproduces the layout comment out of <c>tpic.c</c> in full.
/// <para/>
/// A fixed forty-byte header, then one offset per tile and one more for the attributes behind them,
/// then the tiles end to end, then null-separated <c>name=value</c> strings. Everything in the
/// header is most-significant byte first.
/// <para/>
/// The tiles are a pyramid: tile one is the whole picture reduced to a single tile, and each layer
/// below it holds <c>scale</c> times as many across and down until the last layer is the picture at
/// its own size. Within a layer they run across and then down. That makes the tile count arithmetic
/// rather than a matter of opinion, and this reader requires it to come out — a file whose layers do
/// not add up to the count it states is refused rather than read as far as it goes.
/// <para/>
/// The picture taken is the last layer, which is the picture. XnView takes the first tile instead,
/// so a TilePic of a large scan comes out there as a thumbnail of a few hundred pixels and comes out
/// here at the size the file states.
/// <para/>
/// The format itself says nothing about what a tile holds — its own words are that it "neither
/// specifies nor cares" — so what settles it is the name: <c>.tjp</c> is the JPEG one, and a tile
/// that is not a JPEG is refused rather than guessed at.
/// </remarks>
public readonly record struct TilePicFile : IImageFormatReader<TilePicFile>, IImageToRawImage<TilePicFile> {

  /// <summary>The four bytes a file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => "TPC\n"u8;

  /// <summary>The only header size the format has, and it states it in the file.</summary>
  public const int HeaderSize = 40;

  /// <summary>Bytes one entry of the tile index takes.</summary>
  public const int IndexEntrySize = 4;

  /// <summary>
  /// The most tiles a file is read with, so a corrupt count cannot ask for an index of gigabytes
  /// before anything has been checked.
  /// </summary>
  public const int MaximumTiles = 1 << 20;

  static string IImageFormatMetadata<TilePicFile>.PrimaryExtension => ".tjp";
  static string[] IImageFormatMetadata<TilePicFile>.FileExtensions => [".tjp"];
  static TilePicFile IImageFormatReader<TilePicFile>.FromSpan(ReadOnlySpan<byte> data) => TilePicReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<TilePicFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<TilePicFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  /// <summary>Image width in pixels, as the header states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, as the header states it.</summary>
  public int Height { get; init; }

  /// <summary>The bottom layer's tiles put back together, three bytes a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(TilePicFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
