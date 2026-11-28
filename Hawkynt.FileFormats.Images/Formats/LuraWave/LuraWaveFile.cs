using System;
using FileFormat.Core;

namespace FileFormat.LuraWave;

/// <summary>In-memory representation of a LuraWave (<c>.lwf</c>) image: LuraTech's wavelet still-image codec.</summary>
/// <remarks>
/// Every colour mode LuraTech's decoders render, both stripe layouts, interest regions, byte budgets and passwords
/// decode exactly as the C codec in LuraWave SmartCompress 3.0 decodes them, on streams from LuraTech's own encoder.
/// The writer produces lossless single-stripe streams that both LuraTech decoders return unchanged: 8-bit grey,
/// 16-bit grey, or RGB in the reversible colour transform. See <c>SPEC.md</c> beside this file.
/// </remarks>
[FormatMagicBytes([0x57, 0x56, 0x02])]
[FormatMimeType("image/x-lurawave", "image/x-lwf")]
[VerifiedBy(ConformanceOracle.None)]
public readonly record struct LuraWaveFile :
  IImageFormatReader<LuraWaveFile>, IImageToRawImage<LuraWaveFile>,
  IImageFromRawImage<LuraWaveFile>, IImageFormatWriter<LuraWaveFile>,
  IImageInfoReader<LuraWaveFile> {

  static string IImageFormatMetadata<LuraWaveFile>.PrimaryExtension => ".lwf";
  static string[] IImageFormatMetadata<LuraWaveFile>.FileExtensions => [".lwf"];
  static LuraWaveFile IImageFormatReader<LuraWaveFile>.FromSpan(ReadOnlySpan<byte> data) => LuraWaveReader.FromSpan(data);
  static byte[] IImageFormatWriter<LuraWaveFile>.ToBytes(LuraWaveFile file) => LuraWaveWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The colour model the stream was (or will be) coded in.</summary>
  public LuraWaveColourMode Mode { get; init; }

  /// <summary>The parsed header of a file that was read; <see langword="null"/> for one built from pixels.</summary>
  public LuraWaveHeader? Header { get; init; }

  /// <summary>Decoded pixels in <see cref="PixelFormat"/>: Gray8, big-endian Gray16 or Rgb24 rows without padding.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The layout of <see cref="PixelData"/>, which follows from <see cref="Mode"/>.</summary>
  public PixelFormat PixelFormat => PixelFormatOf(this.Mode);

  /// <summary>Grey modes decode to grey, the wide grey modes to 16 bits, every colour mode to 8-bit RGB.</summary>
  public static PixelFormat PixelFormatOf(LuraWaveColourMode mode) => mode switch {
    LuraWaveColourMode.Grey => PixelFormat.Gray8,
    LuraWaveColourMode.WideGrey or LuraWaveColourMode.WideSignedGrey => PixelFormat.Gray16,
    _ => PixelFormat.Rgb24,
  };

  /// <summary>Reads dimensions and colour mode from the chunked header without decoding any coefficient.</summary>
  /// <param name="header">The start of the file, up to and including its <c>DT</c> chunk header.</param>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    if (!LuraWaveHeader.HasSignature(header))
      return null;

    LuraWaveHeader parsed;
    try {
      parsed = LuraWaveHeader.Parse(header);
    } catch (System.IO.InvalidDataException) {
      return null;
    }

    var bits = PixelFormatOf(parsed.Mode) switch {
      PixelFormat.Gray8 => 8,
      PixelFormat.Gray16 => 16,
      _ => 24,
    };
    var layout = parsed.IsEmbedded ? "embedded" : $"baseline, {parsed.StripeCount} stripes";
    return new(parsed.Width, parsed.Height, bits, parsed.Mode.ToString(), $"LuraWave 5/3 wavelet ({layout})");
  }

  public static RawImage ToRawImage(LuraWaveFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.PixelFormat,
    PixelData = file.PixelData[..],
  };

  /// <summary>Takes grey as 8-bit grey, 16-bit grey as wide grey and everything else as RGB (alpha is dropped: the format has none).</summary>
  public static LuraWaveFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Gray16);
    var mode = image.Format switch {
      PixelFormat.Gray8 => LuraWaveColourMode.Grey,
      PixelFormat.Gray16 => LuraWaveColourMode.WideGrey,
      _ => LuraWaveColourMode.ReversibleColour,
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      Mode = mode,
      PixelData = image.PixelData[..],
    };
  }
}
