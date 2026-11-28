using System;
using FileFormat.Core;

namespace FileFormat.LuraWave;

/// <summary>In-memory representation of a LuraWave (<c>.lwf</c>) image: LuraTech's wavelet still-image codec.</summary>
/// <remarks>
/// Every colour mode, both stripe layouts, interest regions, byte budgets and passwords decode exactly as the C codec
/// in LuraWave SmartCompress 3.0 decodes them, on streams from LuraTech's own encoder. For modes 5, 20 and 21 that
/// codec returns only raw channels; they are kept in <see cref="Channels"/>, and <see cref="PixelData"/> is this
/// implementation's rendering of them. The writer produces lossless single-stripe streams that LuraTech's C decoder
/// returns unchanged: 8-bit grey, 16-bit grey, RGB in the reversible colour transform, RGBA as four raw channels, or the
/// channels of a mode 20 or 21 file that was read. See <c>SPEC.md</c> beside this file.
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

  /// <summary>Decoded pixels in <see cref="PixelFormat"/>: Gray8, big-endian Gray16, Rgb24 or Rgba32 rows without padding.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// The channel samples of the modes LuraTech's decoder returns only raw, exactly as it returns them; <see langword="null"/>
  /// for every other mode.
  /// </summary>
  /// <remarks>
  /// Interleaved, <c>Width·Height·ChannelCount</c> samples, row by row, with the colour transform undone:
  /// <list type="bullet">
  /// <item><see cref="LuraWaveColourMode.RawFourChannel"/> (5): four channels, 0..255 each (its output type 2);</item>
  /// <item><see cref="LuraWaveColourMode.WideLab"/> (20): L, a, b, each 0..M (output type 0x23);</item>
  /// <item><see cref="LuraWaveColourMode.WideFourChannel"/> (21): the four encoder inputs (cyan, magenta, yellow and black
  /// as LuraTech's CMYK quadratic reads them), each 0..M (output type 0x24).</item>
  /// </list>
  /// <c>M</c> is the stream's <c>MV</c> maximum, or 65535 without one. When set on a file in one of these modes, the
  /// writer codes these samples instead of <see cref="PixelData"/>.
  /// </remarks>
  public ushort[]? Channels { get; init; }

  /// <summary>Samples per pixel in <see cref="Channels"/>: the number of channels <see cref="Mode"/> codes.</summary>
  public int ChannelCount => LuraWaveHeader.ChannelsOf(this.Mode);

  /// <summary>The layout of <see cref="PixelData"/>, which follows from <see cref="Mode"/>.</summary>
  public PixelFormat PixelFormat => PixelFormatOf(this.Mode);

  /// <summary>
  /// Grey modes decode to grey, the wide grey modes to 16 bits, the four raw channels of mode 5 to RGBA, every other
  /// colour mode to 8-bit RGB.
  /// </summary>
  public static PixelFormat PixelFormatOf(LuraWaveColourMode mode) => mode switch {
    LuraWaveColourMode.Grey => PixelFormat.Gray8,
    LuraWaveColourMode.WideGrey or LuraWaveColourMode.WideSignedGrey => PixelFormat.Gray16,
    LuraWaveColourMode.RawFourChannel => PixelFormat.Rgba32,
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
      PixelFormat.Rgba32 => 32,
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

  /// <summary>
  /// Takes grey as 8-bit grey, 16-bit grey as wide grey, anything with alpha as RGBA in four raw channels, and
  /// everything else as RGB.
  /// </summary>
  public static LuraWaveFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.Format is PixelFormat.Gray8 or PixelFormat.Gray16 ? image
      : image.EnsureFormat(image.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24);
    var mode = image.Format switch {
      PixelFormat.Gray8 => LuraWaveColourMode.Grey,
      PixelFormat.Gray16 => LuraWaveColourMode.WideGrey,
      PixelFormat.Rgba32 => LuraWaveColourMode.RawFourChannel,
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
