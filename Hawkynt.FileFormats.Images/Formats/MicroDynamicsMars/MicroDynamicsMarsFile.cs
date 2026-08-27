using System;
using FileFormat.Core;

namespace FileFormat.MicroDynamicsMars;

/// <summary>In-memory representation of a Micro Dynamics MARS page (.pbt).</summary>
/// <remarks>
/// MARS was a Macintosh document-archival system that kept scanned pages on optical disk, and no
/// description of what it wrote has ever been published. The layout here comes from XnView's own
/// converter and every field in it was put back to that converter before it was written down: a page
/// built to this header is reported at the size it states and comes back pixel for pixel as the page
/// that was coded.
/// <para/>
/// A big-endian word of two, then <c>PBIT</c>. A big-endian long at six is the resolution, and it is
/// the resolution both ways — the converter stores the one number as the horizontal and the vertical
/// alike. A word at ten is not read. Then the size, big-endian and the unusual way round: the height
/// at twelve and the width at sixteen. The rest of the header is not read; the coded page begins at
/// 512 and is Group 4.
/// <para/>
/// The converter reads no other coding for this name, so no other is offered here.
/// </remarks>
public readonly record struct MicroDynamicsMarsFile
  : IImageFormatReader<MicroDynamicsMarsFile>, IImageToRawImage<MicroDynamicsMarsFile>, IImageFromRawImage<MicroDynamicsMarsFile>, IImageFormatWriter<MicroDynamicsMarsFile> {

  /// <summary>The six bytes a page opens with: a big-endian two, then the format's four letters.</summary>
  public static ReadOnlySpan<byte> Signature => [0x02, 0x00, (byte)'P', (byte)'B', (byte)'I', (byte)'T'];

  /// <summary>Where the resolution stands, as a big-endian long.</summary>
  public const int ResolutionOffset = 6;

  /// <summary>Where the height stands, as a big-endian long.</summary>
  public const int HeightOffset = 12;

  /// <summary>Where the width stands, as a big-endian long.</summary>
  public const int WidthOffset = 16;

  /// <summary>How long the header is, which is where the Group 4 coding begins.</summary>
  public const int HeaderSize = 512;

  /// <summary>The largest side accepted by the decoder.</summary>
  public const int MaximumSide = 65535;

  static string IImageFormatMetadata<MicroDynamicsMarsFile>.PrimaryExtension => ".pbt";
  static string[] IImageFormatMetadata<MicroDynamicsMarsFile>.FileExtensions => [".pbt"];
  static MicroDynamicsMarsFile IImageFormatReader<MicroDynamicsMarsFile>.FromSpan(ReadOnlySpan<byte> data)
    => MicroDynamicsMarsReader.FromSpan(data);
  static byte[] IImageFormatWriter<MicroDynamicsMarsFile>.ToBytes(MicroDynamicsMarsFile file)
    => MicroDynamicsMarsWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<MicroDynamicsMarsFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  static bool? IImageFormatMetadata<MicroDynamicsMarsFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Rows, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Dots an inch, which this format states once and means both ways.</summary>
  public int Resolution { get; init; }

  /// <summary>Packed rows, a set bit being ink.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(MicroDynamicsMarsFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static MicroDynamicsMarsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > MaximumSide || image.Height is < 1 or > MaximumSide)
      throw new ArgumentOutOfRangeException(nameof(image), $"Micro Dynamics MARS dimensions must be between 1 and {MaximumSide} pixels per side.");

    var pixels = BilevelRows.Threshold(image, setWhenDark: true);
    return new() {
      Width = image.Width,
      Height = image.Height,
      Resolution = 300,
      PixelData = BilevelRows.Pack(pixels, image.Width, image.Height),
    };
  }
}
