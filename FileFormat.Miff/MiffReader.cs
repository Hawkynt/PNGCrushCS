using System;
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

  public static MiffFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MiffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid MIFF file.");

    // Verify magic
    var magic = Encoding.ASCII.GetString(data, 0, _MAGIC.Length);
    if (magic != _MAGIC)
      throw new InvalidDataException("Invalid MIFF signature.");

    var fields = MiffHeaderParser.Parse(data, out var dataOffset);

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
      data.AsSpan(dataOffset, Math.Min(paletteSize, data.Length - dataOffset)).CopyTo(palette.AsSpan(0));
      dataOffset += paletteSize;
    }

    // Read pixel data
    var remainingBytes = data.Length - dataOffset;
    var rawData = new byte[remainingBytes];
    data.AsSpan(dataOffset, remainingBytes).CopyTo(rawData.AsSpan(0));

    var channelsPerPixel = _GetChannelsPerPixel(type, colorspace);
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
