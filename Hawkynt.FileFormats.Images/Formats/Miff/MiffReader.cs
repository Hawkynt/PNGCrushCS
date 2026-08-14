using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Miff;

/// <summary>Reads MIFF files from bytes, streams, or file paths.</summary>
public static class MiffReader {

  private const string _MAGIC = "id=ImageMagick";
  private const int _MIN_HEADER_SIZE = 14; // "id=ImageMagick" length

  public static MiffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MIFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MiffFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static MiffFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid MIFF file.");

    // Verify magic, which a leading comment may sit in front of rather than start the file.
    var headerStart = MiffHeaderParser.FindHeaderStart(data);
    if (headerStart + _MAGIC.Length > data.Length)
      throw new InvalidDataException("Invalid MIFF signature.");

    var magic = Encoding.ASCII.GetString(data.Slice(headerStart, _MAGIC.Length));
    if (magic != _MAGIC)
      throw new InvalidDataException("Invalid MIFF signature.");

    // MiffHeaderParser.Parse and MiffRleCompressor.Decompress require byte[]
    var bytes = data.ToArray();
    var fields = MiffHeaderParser.Parse(bytes, out var dataOffset);

    // Extract fields
    var width = fields.TryGetValue("columns", out var colStr) ? int.Parse(colStr) : 0;
    var height = fields.TryGetValue("rows", out var rowStr) ? int.Parse(rowStr) : 0;
    var depth = fields.TryGetValue("depth", out var depthStr) ? int.Parse(depthStr) : 8;
    var colorspace = fields.TryGetValue("colorspace", out var csStr) ? csStr : "sRGB";

    var colorClass = MiffColorClass.DirectClass;
    if (fields.TryGetValue("class", out var classStr) && classStr.Equals("PseudoClass", StringComparison.OrdinalIgnoreCase))
      colorClass = MiffColorClass.PseudoClass;

    // What the pixel is made of, said the way the file says it rather than the way `type` says it.
    var hasAlpha = _HasAlphaChannel(fields);
    var type = _DescribeLayout(colorspace, hasAlpha);

    var compression = MiffCompression.None;
    if (fields.TryGetValue("compression", out var compStr)) {
      if (compStr.Equals("RLE", StringComparison.OrdinalIgnoreCase))
        compression = MiffCompression.Rle;
      else if (compStr.Equals("Zip", StringComparison.OrdinalIgnoreCase))
        compression = MiffCompression.Zip;
    }

    var paletteColorCount = 0;
    if (fields.TryGetValue("colors", out var colorsStr))
      paletteColorCount = int.Parse(colorsStr);

    // Read palette for PseudoClass
    byte[]? palette = null;
    var bytesPerChannel = depth / 8;
    if (colorClass == MiffColorClass.PseudoClass && paletteColorCount > 0) {
      var paletteSize = paletteColorCount * 3 * bytesPerChannel;
      palette = new byte[paletteSize];
      data.Slice(dataOffset, Math.Min(paletteSize, data.Length - dataOffset)).CopyTo(palette.AsSpan(0));
      dataOffset += paletteSize;
    }

    // Read pixel data
    var remainingBytes = data.Length - dataOffset;
    var rawData = new byte[remainingBytes];
    data.Slice(dataOffset, remainingBytes).CopyTo(rawData.AsSpan(0));

    var channelsPerPixel = _GetChannelsPerPixel(colorClass, colorspace, hasAlpha);
    var bytesPerPixel = channelsPerPixel * bytesPerChannel;
    var pixelCount = width * height;

    byte[] pixelData;
    switch (compression) {
      case MiffCompression.Rle:
        pixelData = MiffRleCompressor.Decompress(rawData, bytesPerPixel, pixelCount);
        break;
      case MiffCompression.Zip:
        pixelData = _DecompressZip(rawData, pixelCount * bytesPerPixel);
        break;
      default:
        pixelData = new byte[pixelCount * bytesPerPixel];
        rawData.AsSpan(0, Math.Min(rawData.Length, pixelData.Length)).CopyTo(pixelData.AsSpan(0));
        break;
    }

    pixelData = _NormaliseSamples(pixelData, depth, fields);

    return new MiffFile {
      Width = width,
      Height = height,
      Depth = depth,
      ColorClass = colorClass,
      Compression = compression,
      Colorspace = colorspace,
      Type = type,
      PixelData = pixelData,
      Palette = palette
    };
  
  }

  public static MiffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Turns the samples into unsigned integers of the stated depth, whatever they were.</summary>
  /// <remarks>
  /// A MIFF says how its samples are held in <c>quantum:format</c>, and a high-dynamic-range
  /// ImageMagick writes <c>floating-point</c> for anything it keeps as real numbers — which includes
  /// every picture it has taken through a colourspace, so <c>magick x.png -colorspace Gray x.miff</c>
  /// is one. Those are IEEE floats scaled so that one is white: half at depth sixteen, single at
  /// thirty-two, double at sixty-four.
  /// <para/>
  /// Read as integers instead they are not noise but a curve — the first sample of the reference
  /// grey file is 0x2C9F, which is 0.0722 and therefore 18 of 255, where its leading byte is 44. The
  /// picture that comes out is plausible and wrong, which is the failure this reader must not have;
  /// against ImageMagick's own reading of a 61x37 sample it measured 688 of 2257 pixels different.
  /// <para/>
  /// Converted here rather than at the point of use so that everything downstream — the narrowing to
  /// eight bits, the palette, the writer — sees the one representation the rest of the format uses.
  /// Rounding to the stated depth reproduces what ImageMagick itself puts in the file when asked for
  /// unsigned samples: 0x2C9F becomes 0x127C, byte for byte.
  /// </remarks>
  private static byte[] _NormaliseSamples(byte[] pixelData, int depth, Dictionary<string, string> fields) {
    if (!fields.TryGetValue("quantum:format", out var format))
      return pixelData;

    if (format.Equals("unsigned", StringComparison.OrdinalIgnoreCase))
      return pixelData;

    if (!format.Equals("floating-point", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException($"MIFF quantum:format '{format}' is not supported; only unsigned and floating-point samples are read.");

    var bytesPerSample = depth / 8;
    if (bytesPerSample is not (2 or 4 or 8))
      throw new InvalidDataException($"MIFF floating-point samples at depth {depth} are not supported.");

    var sampleCount = pixelData.Length / bytesPerSample;
    var result = new byte[sampleCount * bytesPerSample];
    var span = pixelData.AsSpan();

    for (var i = 0; i < sampleCount; ++i) {
      var at = i * bytesPerSample;
      var value = bytesPerSample switch {
        2 => (double)BinaryPrimitives.ReadHalfBigEndian(span[at..]),
        4 => BinaryPrimitives.ReadSingleBigEndian(span[at..]),
        _ => BinaryPrimitives.ReadDoubleBigEndian(span[at..]),
      };

      // High dynamic range is what the floating-point form is for, so a sample may sit outside the
      // displayable range; the integer form has nowhere to put those.
      if (double.IsNaN(value) || value <= 0)
        continue;

      var target = result.AsSpan(at, bytesPerSample);
      if (value >= 1) {
        target.Fill(0xFF);
        continue;
      }

      switch (bytesPerSample) {
        case 2:
          BinaryPrimitives.WriteUInt16BigEndian(target, (ushort)Math.Round(value * ushort.MaxValue, MidpointRounding.AwayFromZero));
          break;
        case 4:
          BinaryPrimitives.WriteUInt32BigEndian(target, (uint)Math.Round(value * uint.MaxValue, MidpointRounding.AwayFromZero));
          break;
        default:
          BinaryPrimitives.WriteUInt64BigEndian(target, (ulong)Math.Round(value * ulong.MaxValue, MidpointRounding.AwayFromZero));
          break;
      }
    }

    return result;
  }

  /// <summary>Whether a pixel carries an alpha sample, taken from the fields that state it.</summary>
  /// <remarks>
  /// <c>alpha-trait</c> is the modern field and <c>matte</c> the one that predates it; ImageMagick
  /// writes both and reads either. Neither being present means no alpha, which is what an absent
  /// <c>alpha-trait</c> means to ImageMagick as well.
  /// </remarks>
  private static bool _HasAlphaChannel(Dictionary<string, string> fields) {
    if (fields.TryGetValue("alpha-trait", out var trait))
      return !trait.Equals("Undefined", StringComparison.OrdinalIgnoreCase);

    return fields.TryGetValue("matte", out var matte) && matte.Equals("True", StringComparison.OrdinalIgnoreCase);
  }

  private static bool _IsGrayColorspace(string colorspace)
    => colorspace.Equals("Gray", StringComparison.OrdinalIgnoreCase)
       || colorspace.Equals("LinearGray", StringComparison.OrdinalIgnoreCase);

  private static bool _IsCmykColorspace(string colorspace)
    => colorspace.Equals("CMYK", StringComparison.OrdinalIgnoreCase);

  /// <summary>Names the pixel layout the header describes, for everything downstream.</summary>
  /// <remarks>
  /// The <c>type</c> line is not this answer and cannot be: ImageMagick's own files with an alpha
  /// channel have no <c>type</c> line, so believing it means falling back to a default of TrueColor
  /// and reading four samples three at a time. It is derived here instead, once, so that the channel
  /// count and the pixel format downstream cannot disagree about the same file — which is the shape
  /// the fault took.
  /// </remarks>
  private static string _DescribeLayout(string colorspace, bool hasAlpha) {
    if (_IsGrayColorspace(colorspace))
      return hasAlpha ? "GrayscaleAlpha" : "Grayscale";

    if (_IsCmykColorspace(colorspace))
      return hasAlpha ? "CMYKAlpha" : "CMYK";

    return hasAlpha ? "TrueColorAlpha" : "TrueColor";
  }

  /// <summary>How many samples a pixel holds, by the rule ImageMagick sizes its own packet with.</summary>
  /// <remarks>
  /// Taken from its reader in that order: one sample to begin with, three if the class is
  /// DirectClass, one again if the colourspace is grey, then one more for an alpha channel and one
  /// more for CMYK. A palette file is one sample whatever its colourspace says, because the sample
  /// is an index.
  /// <para/>
  /// Checked against the <c>number-channels</c> ImageMagick states in its own headers: 1 for grey,
  /// 2 for grey with alpha, 3 for truecolour, 4 for truecolour with alpha, 5 for CMYK with alpha and
  /// 5 for a palette with alpha — the last of which counts the colormap's three, where the packet
  /// holds two.
  /// </remarks>
  private static int _GetChannelsPerPixel(MiffColorClass colorClass, string colorspace, bool hasAlpha) {
    var channels = 1;
    if (colorClass == MiffColorClass.DirectClass)
      channels = _IsGrayColorspace(colorspace) ? 1 : 3;

    if (hasAlpha)
      ++channels;

    if (colorClass == MiffColorClass.DirectClass && _IsCmykColorspace(colorspace))
      ++channels;

    return channels;
  }

  private static byte[] _DecompressZip(byte[] compressedData, int expectedSize) {
    using var inputStream = new MemoryStream(compressedData);
    using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
    using var outputStream = new MemoryStream();
    deflateStream.CopyTo(outputStream);
    var decompressed = outputStream.ToArray();

    var result = new byte[expectedSize];
    decompressed.AsSpan(0, Math.Min(decompressed.Length, expectedSize)).CopyTo(result.AsSpan(0));
    return result;
  }
}
