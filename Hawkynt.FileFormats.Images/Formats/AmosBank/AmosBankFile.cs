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
  : IImageFormatReader<AmosBankFile>, IImageToRawImage<AmosBankFile> {

  /// <summary>Colours an Amiga OCS palette holds.</summary>
  public const int ColorCount = 32;

  /// <summary>The two bytes every bank starts with.</summary>
  public const string Signature = "Am";

  static string IImageFormatMetadata<AmosBankFile>.PrimaryExtension => ".abk";
  static string[] IImageFormatMetadata<AmosBankFile>.FileExtensions => [".abk"];
  static AmosBankFile IImageFormatReader<AmosBankFile>.FromSpan(ReadOnlySpan<byte> data)
    => AmosBankReader.FromSpan(data);
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
}
