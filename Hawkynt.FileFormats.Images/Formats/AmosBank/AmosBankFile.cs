using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.AmosBank;

/// <summary>In-memory representation of an AMOS memory bank (.abk).</summary>
/// <remarks>
/// AMOS was a BASIC for the Amiga, and a bank is a lump of its memory saved to disk. Two kinds hold
/// pictures: a packed screen, and a set of sprites or icons laid out side by side.
/// <para/>
/// The packed screen's encoding is three streams read in step rather than one — a stream of bytes,
/// a stream of bits saying when to take the next byte, and a third stream of bits saying when to
/// take the next byte of the second. Compressing the control stream as well is what makes the
/// scheme worth its complexity on a machine where a screen is a hundred kilobytes.
/// </remarks>
public readonly record struct AmosBankFile
  : IImageFormatReader<AmosBankFile>, IImageToRawImage<AmosBankFile>,
    IImageFromRawImage<AmosBankFile>, IImageFormatWriter<AmosBankFile> {

  /// <summary>Colours an Amiga OCS palette holds.</summary>
  public const int ColorCount = 32;

  /// <summary>Bitplanes thirty-two colours need.</summary>
  public const int Planes = 5;

  /// <summary>Pixels the hardware fetches at a time, and so the step a sprite's width comes in.</summary>
  public const int WidthStep = 16;

  /// <summary>The two bytes every bank starts with.</summary>
  public const string Signature = "Am";

  static string IImageFormatMetadata<AmosBankFile>.PrimaryExtension => ".abk";
  static string[] IImageFormatMetadata<AmosBankFile>.FileExtensions => [".abk"];
  static AmosBankFile IImageFormatReader<AmosBankFile>.FromSpan(ReadOnlySpan<byte> data)
    => AmosBankReader.FromSpan(data);
  static byte[] IImageFormatWriter<AmosBankFile>.ToBytes(AmosBankFile file) => AmosBankWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AmosBankFile>.VideoModes => [
    new("AMOS bank", [(IntegerRange.Any, IntegerRange.Any)], [ColorCount])
  ];

  /// <summary>The picture, one palette index per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The palette as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(AmosBankFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData,
    Palette = file.Palette,
    PaletteCount = ColorCount,
  };

  /// <summary>
  /// Converts an Amiga OCS palette, which stores four bits a channel across two bytes.
  /// </summary>
  /// <remarks>
  /// The channels are widened by multiplying the whole packed value by seventeen at once, which is
  /// bit replication done in one step — the same answer as repeating each nibble into the byte
  /// below it, because the three channels never carry into each other.
  /// </remarks>
  public static byte[] ReadPalette(ReadOnlySpan<byte> data, int offset) {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var at = offset + i * 2;
      if (at + 1 >= data.Length)
        break;

      palette[i * 3] = ChannelScaling.Expand4(data[at] & 15);
      palette[i * 3 + 1] = ChannelScaling.Expand4(data[at + 1] >> 4);
      palette[i * 3 + 2] = ChannelScaling.Expand4(data[at + 1] & 15);
    }

    return palette;
  }

  /// <summary>Encodes a picture as a bank of one sprite.</summary>
  /// <remarks>
  /// Of the two kinds that hold pictures the sprite bank is the one written. The packed screen is a
  /// compressor with three streams read in step, and a bank of one sprite says the same picture in
  /// bytes any AMOS program can read — so the packing would be work spent on a file that is no more
  /// readable for it.
  /// <para/>
  /// A sprite's width counts sixteen-pixel words because that is how the hardware fetches them, so a
  /// picture whose width is not a multiple of sixteen is sampled to the nearest one that is rather
  /// than padded: padding would put columns in the file that the picture does not have and that
  /// anything reading it would show.
  /// </remarks>
  public static AmosBankFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Max(WidthStep, (image.Width + WidthStep / 2) / WidthStep * WidthStep);
    var height = Math.Max(1, image.Height);
    var source = image.Width == width && image.Height == height ? image : image.SampleTo(width, height);
    var indexed = source.EnsureIndexedAtMost(ColorCount);

    // Four bits a channel is what a bank stores, so the palette is reduced before the pixels are
    // mapped onto it and two entries that would have collapsed afterwards collapse before.
    var palette = new byte[ColorCount * 3];
    var stated = indexed.Palette ?? [];
    for (var i = 0; i < palette.Length && i < stated.Length; ++i)
      palette[i] = ChannelScaling.Expand4((stated[i] * 15 + 127) / 255);

    return new() { PixelData = indexed.PixelData, Palette = palette, Width = width, Height = height };
  }
}
