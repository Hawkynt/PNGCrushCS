using System;
using FileFormat.Core;

namespace FileFormat.Cineon;

/// <summary>In-memory representation of a Cineon image.</summary>
[FormatMagicBytes([0x80, 0x2A, 0x5F, 0xD7])]
public sealed class CineonFile :
  IImageFormatReader<CineonFile>, IImageToRawImage<CineonFile>,
  IImageFromRawImage<CineonFile>, IImageFormatWriter<CineonFile> {

  static string IImageFormatMetadata<CineonFile>.PrimaryExtension => ".cin";
  static string[] IImageFormatMetadata<CineonFile>.FileExtensions => [".cin"];
  static CineonFile IImageFormatReader<CineonFile>.FromSpan(ReadOnlySpan<byte> data) => CineonReader.FromSpan(data);
  static byte[] IImageFormatWriter<CineonFile>.ToBytes(CineonFile file) => CineonWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerSample { get; init; }
  public byte Orientation { get; init; }
  public int ImageDataOffset { get; init; }
  public byte[] PixelData { get; init; } = [];

  /// <summary>Reference black: the code value a Cineon file uses for no exposure at all.</summary>
  private const double _REFERENCE_BLACK = 95;

  /// <summary>Reference white: the code value for a fully exposed diffuse white.</summary>
  private const double _REFERENCE_WHITE = 685;

  /// <summary>
  /// Code values per decade of exposure: one step is 0.002 in printing density, and the film gamma
  /// the scale is quoted against is 0.6, so a factor of ten in light is 0.6 / 0.002 codes.
  /// </summary>
  private const double _CODES_PER_DECADE = 300;

  /// <summary>Maps each of the 1024 code values to the display value it stands for.</summary>
  private static readonly ushort[] _DISPLAY_FROM_CODE = _BuildDisplayTable();

  /// <summary>Converts a Cineon image to a 16-bit <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(CineonFile file) {
    // 10 bits a sample packed three to a 32-bit word is the classic Cineon and what the code below
    // reads, but the format allows 8, 12 and 16 as well, and 8 is what a modern writer produces for
    // an ordinary 8-bit source. Those were refused outright; they are simply unpacked samples.
    if (file.BitsPerSample is 8 or 16)
      return _FromWholeSamples(file);

    if (file.BitsPerSample != 10)
      throw new NotSupportedException(
        $"Cineon bit depth {file.BitsPerSample} is not supported; 8, 10 and 16 are.");

    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;
    var pixelCount = width * height;
    var result = new byte[pixelCount * 6];

    for (var i = 0; i < pixelCount; ++i) {
      var offset = i * 4;
      var word = (uint)(src[offset] << 24 | src[offset + 1] << 16 | src[offset + 2] << 8 | src[offset + 3]);
      var r = _DISPLAY_FROM_CODE[(word >> 22) & 0x3FF];
      var g = _DISPLAY_FROM_CODE[(word >> 12) & 0x3FF];
      var b = _DISPLAY_FROM_CODE[(word >> 2) & 0x3FF];
      var di = i * 6;
      result[di] = (byte)(r >> 8);
      result[di + 1] = (byte)r;
      result[di + 2] = (byte)(g >> 8);
      result[di + 3] = (byte)g;
      result[di + 4] = (byte)(b >> 8);
      result[di + 5] = (byte)b;
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb48,
      PixelData = result,
    };
  }

  /// <summary>Creates a 10-bit linear Cineon image from a <see cref="RawImage"/>. Accepts Rgb48 natively or any convertible format.</summary>
  public static CineonFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb48 = PixelConverter.Convert(image, PixelFormat.Rgb48);
    var width = rgb48.Width;
    var height = rgb48.Height;
    var src = rgb48.PixelData;
    var pixelCount = width * height;
    var packed = new byte[pixelCount * 4];

    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 6;
      // Read BE uint16 channels and put each back on the printing-density scale.
      var r = _CodeFromDisplay((ushort)((src[si] << 8) | src[si + 1]));
      var g = _CodeFromDisplay((ushort)((src[si + 2] << 8) | src[si + 3]));
      var b = _CodeFromDisplay((ushort)((src[si + 4] << 8) | src[si + 5]));
      // Pack into big-endian 32-bit word: R[31:22] G[21:12] B[11:2] padding[1:0]
      var word = (r << 22) | (g << 12) | (b << 2);
      var di = i * 4;
      packed[di] = (byte)(word >> 24);
      packed[di + 1] = (byte)(word >> 16);
      packed[di + 2] = (byte)(word >> 8);
      packed[di + 3] = (byte)word;
    }

    return new() {
      Width = width,
      Height = height,
      BitsPerSample = 10,
      Orientation = 0,
      ImageDataOffset = 0,
      PixelData = packed,
    };
  }

  /// <summary>
  /// Builds the table that turns a Cineon code value into the display value it stands for.
  /// </summary>
  /// <remarks>
  /// A Cineon file does not hold brightness; it holds printing density, which is what a film scanner
  /// measures. The code values run on a logarithmic scale with reference black at 95 and reference
  /// white at 685, and they were being handed on as though they were ordinary samples — so a picture
  /// came out with its blacks lifted to a flat grey and its whites pulled down, the washed-out look
  /// of an unconverted film scan. Pure red read back as (171, 24, 24), those two numbers being
  /// nothing but the reference points themselves in eight bits.
  ///
  /// The exposure a code stands for is 10^((code - 685) / 300); reference black is not quite zero
  /// exposure, so its share is subtracted off and the rest stretched back over the full range. The
  /// result is linear light, which is then given the sRGB curve so it is ready to look at.
  ///
  /// Checked against ImageMagick over a full 0..255 ramp: every step comes back within one count.
  /// </remarks>
  private static ushort[] _BuildDisplayTable() {
    var table = new ushort[1024];
    var blackExposure = Math.Pow(10, (_REFERENCE_BLACK - _REFERENCE_WHITE) / _CODES_PER_DECADE);

    for (var code = 0; code < table.Length; ++code) {
      var exposure = Math.Pow(10, (code - _REFERENCE_WHITE) / _CODES_PER_DECADE);
      var linear = Math.Clamp((exposure - blackExposure) / (1 - blackExposure), 0, 1);
      table[code] = (ushort)Math.Round(_SrgbFromLinear(linear) * 65535);
    }

    return table;
  }

  /// <summary>The inverse of <see cref="_BuildDisplayTable"/>: a display value back to a code value.</summary>
  private static uint _CodeFromDisplay(ushort value) {
    var blackExposure = Math.Pow(10, (_REFERENCE_BLACK - _REFERENCE_WHITE) / _CODES_PER_DECADE);
    var linear = _LinearFromSrgb(value / 65535.0);
    var exposure = (linear * (1 - blackExposure)) + blackExposure;
    var code = _REFERENCE_WHITE + (_CODES_PER_DECADE * Math.Log10(exposure));

    return (uint)Math.Clamp(Math.Round(code), 0, 1023);
  }

  private static double _SrgbFromLinear(double linear)
    => linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;

  private static double _LinearFromSrgb(double srgb)
    => srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);

  /// <summary>
  /// The depths whose samples sit on byte boundaries — 8 and 16 bits — read straight out as RGB.
  /// </summary>
  /// <remarks>
  /// Cineon is big-endian, so a 16-bit sample is already in the order Rgb48 wants; an 8-bit one is
  /// scaled up to it so every depth leaves this type in the same format.
  /// </remarks>
  private static RawImage _FromWholeSamples(CineonFile file) {
    var pixelCount = file.Width * file.Height;
    var source = file.PixelData;
    var result = new byte[pixelCount * 6];
    var bytesPerSample = file.BitsPerSample / 8;

    for (var i = 0; i < pixelCount; ++i) {
      for (var channel = 0; channel < 3; ++channel) {
        var at = ((i * 3) + channel) * bytesPerSample;
        int code;
        if (bytesPerSample == 1)
          code = at < source.Length ? source[at] * 1023 / 255 : 0;
        else
          code = at + 1 < source.Length ? (((source[at] << 8) | source[at + 1]) * 1023 / 65535) : 0;

        var value = _DISPLAY_FROM_CODE[code];
        var target = (i * 6) + (channel * 2);
        result[target] = (byte)(value >> 8);
        result[target + 1] = (byte)value;
      }
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb48,
      PixelData = result,
    };
  }
}
