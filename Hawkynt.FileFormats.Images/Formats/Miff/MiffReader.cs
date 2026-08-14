using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
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
    var type = fields.TryGetValue("type", out var typeStr) ? typeStr : "TrueColor";
    var colorspace = fields.TryGetValue("colorspace", out var csStr) ? csStr : "sRGB";

    var colorClass = MiffColorClass.DirectClass;
    if (fields.TryGetValue("class", out var classStr) && classStr.Equals("PseudoClass", StringComparison.OrdinalIgnoreCase))
      colorClass = MiffColorClass.PseudoClass;

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

    var channelsPerPixel = _GetChannelsPerPixel(type, colorspace);
    var bytesPerPixel = channelsPerPixel * bytesPerChannel;
    var pixelCount = width * height;

    byte[] pixelData;
    switch (compression) {
      case MiffCompression.Rle:
        pixelData = MiffRleCompressor.Decompress(rawData, bytesPerPixel, pixelCount);
        break;
      case MiffCompression.Zip:
        pixelData = _DecompressZip(rawData, pixelCount * bytesPerPixel, height, _StatesRowChunks(fields));
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

  private static int _GetChannelsPerPixel(string type, string colorspace) {
    if (colorspace.Equals("CMYK", StringComparison.OrdinalIgnoreCase))
      return type.Contains("Alpha", StringComparison.OrdinalIgnoreCase) ? 5 : 4;

    if (type.Contains("Alpha", StringComparison.OrdinalIgnoreCase))
      return type.StartsWith("Grayscale", StringComparison.OrdinalIgnoreCase) ? 2 :
             type.StartsWith("Palette", StringComparison.OrdinalIgnoreCase) ? 2 : 4;

    if (type.StartsWith("Grayscale", StringComparison.OrdinalIgnoreCase))
      return 1;

    if (type.StartsWith("Palette", StringComparison.OrdinalIgnoreCase))
      return 1;

    // TrueColor, default
    return 3;
  }

  /// <summary>Whether the Zip payload is cut into one length-prefixed chunk per row.</summary>
  /// <remarks>
  /// The id line carries a version — <c>id=ImageMagick version=1.0</c> — and ImageMagick's reader
  /// takes a four-byte big-endian length before each row for any version above zero. A file that
  /// states no version has no lengths; ImageMagick refuses a Zip payload in that shape, and it is
  /// read here as the plain stream it would have to be.
  /// </remarks>
  private static bool _StatesRowChunks(Dictionary<string, string> fields)
    => fields.TryGetValue("version", out var version)
       && double.TryParse(version, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
       && number != 0;

  /// <summary>Joins the per-row chunks back into the one zlib stream they were cut from.</summary>
  /// <remarks>
  /// ImageMagick flushes its deflater at the end of every row and writes what came out with its
  /// length in front, so a chunk ends at a flush boundary — <c>00 00 FF FF</c> — rather than being a
  /// stream of its own. Inflating a chunk alone therefore fails; the concatenation is what inflates,
  /// and it is what its reader feeds its inflater one chunk at a time.
  /// <para/>
  /// A stated length that runs past the end of the file is the file being wrong about itself, which
  /// is worth saying rather than inflating whatever happens to be there.
  /// </remarks>
  private static byte[] _JoinRowChunks(byte[] payload, int rows) {
    using var joined = new MemoryStream(payload.Length);
    var at = 0;
    for (var row = 0; row < rows; ++row) {
      if (at + sizeof(uint) > payload.Length)
        throw new InvalidDataException($"A Zip-compressed MIFF ends after {row} of its {rows} rows.");

      var length = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(at));
      at += sizeof(uint);
      if (length > (uint)(payload.Length - at))
        throw new InvalidDataException($"A Zip-compressed MIFF states a {length} byte row where {payload.Length - at} bytes are left.");

      joined.Write(payload, at, (int)length);
      at += (int)length;
    }

    return joined.ToArray();
  }

  /// <summary>Inflates the Zip payload, which is a zlib stream and not a raw deflate one.</summary>
  /// <remarks>
  /// It was handed to a raw <see cref="DeflateStream"/>, which finds no zlib header where one is, so
  /// every Zip-compressed MIFF failed outright — one of eleven reference samples would not decode at
  /// all.
  /// <para/>
  /// The stream is read to exactly the size the header accounts for and no further, because
  /// ImageMagick never finishes it: it flushes after the last row and stops, so there is no final
  /// block and no Adler-32 trailer to arrive at. Reading past the last sample would be reading for
  /// an end that was never written.
  /// </remarks>
  private static byte[] _DecompressZip(byte[] compressedData, int expectedSize, int rows, bool rowChunks) {
    var payload = rowChunks ? _JoinRowChunks(compressedData, rows) : compressedData;

    using var inputStream = new MemoryStream(payload);
    using var zlibStream = new ZLibStream(inputStream, CompressionMode.Decompress);

    var result = new byte[expectedSize];
    var filled = 0;
    while (filled < expectedSize) {
      int read;
      try {
        read = zlibStream.Read(result, filled, expectedSize - filled);
      } catch (InvalidDataException e) {
        throw new InvalidDataException($"A Zip-compressed MIFF does not inflate: {e.Message}", e);
      }

      if (read <= 0)
        break;

      filled += read;
    }

    if (filled != expectedSize)
      throw new InvalidDataException($"A Zip-compressed MIFF inflates to {filled} bytes where its size calls for {expectedSize}.");

    return result;
  }
}
