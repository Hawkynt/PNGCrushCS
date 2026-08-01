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

  /// <summary>Converts a Cineon image to a 16-bit <see cref="RawImage"/> by scaling 10-bit values to Rgb48.</summary>
  /// <summary>Reference white: the code that stands for a fully exposed frame.</summary>
  private const int _ReferenceWhite = 685;

  /// <summary>Reference black: below this the negative holds nothing.</summary>
  private const int _ReferenceBlack = 95;

  /// <summary>
  /// How many codes it takes to move one decade of density: 0.002 density a code over a gamma of
  /// 0.6, which is 300 codes for a factor of ten in light.
  /// </summary>
  private const double _CodesPerDecade = 300.0;

  private static readonly ushort[] _DensityToDisplay = _BuildDensityTable();

  /// <summary>Turns one printing-density code into a display-referred sample.</summary>
  /// <remarks>
  /// A Cineon file holds printing density off a film negative, not light: the codes are logarithmic,
  /// and the reference white sits at 685 rather than at the top of the range. Scaling them straight
  /// on to the output — which is what this used to do — leaves the picture squashed into the middle
  /// of the range with its contrast flattened, and no amount of looking at it alone shows that up.
  /// </remarks>
  private static ushort _FromPrintingDensity(int code)
    => _DensityToDisplay[Math.Clamp(code, 0, 1023)];

  private static ushort[] _BuildDensityTable() {
    var table = new ushort[1024];

    // The density at reference black is not zero light, so the range is rescaled to put it there.
    // Without that the darkest part of a picture sits a visible step above black.
    var atBlack = Math.Pow(10.0, (_ReferenceBlack - _ReferenceWhite) / _CodesPerDecade);
    var span = 1.0 - atBlack;

    for (var code = _ReferenceBlack; code < table.Length; ++code) {
      var linear = (Math.Pow(10.0, (code - _ReferenceWhite) / _CodesPerDecade) - atBlack) / span;
      table[code] = (ushort)Math.Clamp(_ToDisplay(linear) * 65535.0 + 0.5, 0, 65535);
    }

    return table;
  }

  /// <summary>The sRGB transfer, which is what everything else in this tree holds.</summary>
  private static double _ToDisplay(double linear) {
    if (linear <= 0.0)
      return 0.0;
    if (linear >= 1.0)
      return 1.0;

    return linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
  }

  public static RawImage ToRawImage(CineonFile file) {
    // Ten bits packed three to a word is what the format was made for, but eight and sixteen are
    // both written in practice and neither needs unpacking — the samples are already whole bytes.
    if (file.BitsPerSample is 8 or 16)
      return _FromWholeBytes(file);

    if (file.BitsPerSample != 10)
      throw new NotSupportedException($"Cineon bit depth {file.BitsPerSample} is not supported; 8, 10 and 16 are.");

    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;
    var pixelCount = width * height;
    var result = new byte[pixelCount * 6];

    for (var i = 0; i < pixelCount; ++i) {
      var offset = i * 4;
      var word = (uint)(src[offset] << 24 | src[offset + 1] << 16 | src[offset + 2] << 8 | src[offset + 3]);
      var r = _FromPrintingDensity((int)((word >> 22) & 0x3FF));
      var g = _FromPrintingDensity((int)((word >> 12) & 0x3FF));
      var b = _FromPrintingDensity((int)((word >> 2) & 0x3FF));
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
      // Read BE uint16 channels, scale 16-bit to 10-bit
      var r = (uint)(((src[si] << 8) | src[si + 1]) * 1023 / 65535);
      var g = (uint)(((src[si + 2] << 8) | src[si + 3]) * 1023 / 65535);
      var b = (uint)(((src[si + 4] << 8) | src[si + 5]) * 1023 / 65535);
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

  /// <summary>Builds a picture from samples that already occupy whole bytes.</summary>
  /// <remarks>
  /// The packing is what makes ten bits awkward — three samples share a thirty-two bit word with
  /// two bits left over. Eight and sixteen need none of that, so they are read straight; the format
  /// stores the wide ones most significant byte first, whatever the machine.
  /// </remarks>
  /// <summary>Reads the depths whose samples are already whole bytes, density and all.</summary>
  /// <remarks>
  /// The codes are printing density whatever their width, so they go through the same curve as the
  /// packed ten-bit ones — scaled into that range first, since the curve is defined against it.
  /// </remarks>
  private static RawImage _FromWholeBytes(CineonFile file) {
    var count = file.Width * file.Height * 3;
    var source = file.PixelData;
    var isDeep = file.BitsPerSample == 16;
    var pixels = new byte[count * 2];

    for (var i = 0; i < count; ++i) {
      var at = isDeep ? i * 2 : i;
      if (at + (isDeep ? 1 : 0) >= source.Length)
        break;

      var raw = isDeep ? (source[at] << 8) | source[at + 1] : source[at];
      var code = raw * 1023 / (isDeep ? 65535 : 255);
      var value = _FromPrintingDensity(code);

      pixels[i * 2] = (byte)(value >> 8);
      pixels[i * 2 + 1] = (byte)value;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb48,
      PixelData = pixels,
    };
  }
}
