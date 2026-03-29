using System;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text;

namespace FileFormat.Nrrd;

/// <summary>Reads NRRD files from bytes, streams, or file paths.</summary>
public static class NrrdReader {

  private const int _MIN_FILE_SIZE = 8; // "NRRD0001" minimum

  public static NrrdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NRRD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NrrdFile FromStream(Stream stream) {
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

  public static NrrdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data is too small to be a valid NRRD file.");

    // Validate magic
    var magic = Encoding.ASCII.GetString(data, 0, 4);
    if (magic != "NRRD")
      throw new InvalidDataException($"Invalid NRRD magic: expected 'NRRD', got '{magic}'.");

    var dataOffset = NrrdHeaderParser.FindDataOffset(data);
    var headerText = Encoding.ASCII.GetString(data, 0, dataOffset);
    var fields = NrrdHeaderParser.Parse(headerText);

    // Parse required fields
    if (!fields.TryGetValue("type", out var typeStr))
      throw new InvalidDataException("Missing required NRRD field: 'type'.");

    if (!fields.TryGetValue("sizes", out var sizesStr))
      throw new InvalidDataException("Missing required NRRD field: 'sizes'.");

    var dataType = NrrdHeaderParser.ParseType(typeStr);
    var sizes = NrrdHeaderParser.ParseSizes(sizesStr);

    var encoding = NrrdEncoding.Raw;
    if (fields.TryGetValue("encoding", out var encodingStr))
      encoding = NrrdHeaderParser.ParseEncoding(encodingStr);

    var endian = "little";
    if (fields.TryGetValue("endian", out var endianStr))
      endian = endianStr.ToLowerInvariant();

    var spacings = Array.Empty<double>();
    if (fields.TryGetValue("spacings", out var spacingsStr))
      spacings = NrrdHeaderParser.ParseSpacings(spacingsStr);

    var labels = Array.Empty<string>();
    if (fields.TryGetValue("labels", out var labelsStr))
      labels = _ParseLabels(labelsStr);

    var rawData = new byte[data.Length - dataOffset];
    data.AsSpan(dataOffset, rawData.Length).CopyTo(rawData.AsSpan(0));

    var pixelData = _DecodeData(rawData, encoding, dataType, sizes, endian);

    return new NrrdFile {
      Sizes = sizes,
      DataType = dataType,
      Encoding = encoding,
      Endian = endian,
      Spacings = spacings,
      PixelData = pixelData,
      Labels = labels
    };
  }

  private static byte[] _DecodeData(byte[] rawData, NrrdEncoding encoding, NrrdType dataType, int[] sizes, string endian) => encoding switch {
    NrrdEncoding.Raw => _CopyRaw(rawData),
    NrrdEncoding.Gzip => _DecompressGzip(rawData),
    NrrdEncoding.Ascii => _DecodeAscii(rawData, dataType, sizes, endian),
    NrrdEncoding.Hex => _DecodeHex(rawData),
    _ => throw new InvalidDataException($"Unsupported NRRD encoding: {encoding}.")
  };

  private static byte[] _CopyRaw(byte[] rawData) {
    var result = new byte[rawData.Length];
    rawData.AsSpan(0, rawData.Length).CopyTo(result);
    return result;
  }

  private static byte[] _DecompressGzip(byte[] compressedData) {
    using var input = new MemoryStream(compressedData);
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    gzip.CopyTo(output);
    return output.ToArray();
  }

  private static byte[] _DecodeAscii(byte[] rawData, NrrdType dataType, int[] sizes, string endian) {
    var text = Encoding.ASCII.GetString(rawData);
    var parts = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    var bytesPerElement = _BytesPerElement(dataType);
    var result = new byte[parts.Length * bytesPerElement];

    for (var i = 0; i < parts.Length; ++i) {
      var bytes = _ParseAsciiElement(parts[i], dataType);
      if (endian == "big")
        Array.Reverse(bytes);

      bytes.AsSpan(0, bytesPerElement).CopyTo(result.AsSpan(i * bytesPerElement));
    }

    return result;
  }

  private static byte[] _ParseAsciiElement(string text, NrrdType dataType) => dataType switch {
    NrrdType.Int8 => [unchecked((byte)sbyte.Parse(text, CultureInfo.InvariantCulture))],
    NrrdType.UInt8 => [byte.Parse(text, CultureInfo.InvariantCulture)],
    NrrdType.Int16 => BitConverter.GetBytes(short.Parse(text, CultureInfo.InvariantCulture)),
    NrrdType.UInt16 => BitConverter.GetBytes(ushort.Parse(text, CultureInfo.InvariantCulture)),
    NrrdType.Int32 => BitConverter.GetBytes(int.Parse(text, CultureInfo.InvariantCulture)),
    NrrdType.UInt32 => BitConverter.GetBytes(uint.Parse(text, CultureInfo.InvariantCulture)),
    NrrdType.Float => BitConverter.GetBytes(float.Parse(text, CultureInfo.InvariantCulture)),
    NrrdType.Double => BitConverter.GetBytes(double.Parse(text, CultureInfo.InvariantCulture)),
    _ => throw new InvalidDataException($"Unknown NRRD type: {dataType}.")
  };

  private static byte[] _DecodeHex(byte[] rawData) {
    var text = Encoding.ASCII.GetString(rawData).Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
    if (text.Length % 2 != 0)
      throw new InvalidDataException("Hex-encoded NRRD data has odd length.");

    var result = new byte[text.Length / 2];
    for (var i = 0; i < result.Length; ++i)
      result[i] = byte.Parse(text.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    return result;
  }

  private static int _BytesPerElement(NrrdType dataType) => dataType switch {
    NrrdType.Int8 or NrrdType.UInt8 => 1,
    NrrdType.Int16 or NrrdType.UInt16 => 2,
    NrrdType.Int32 or NrrdType.UInt32 or NrrdType.Float => 4,
    NrrdType.Double => 8,
    _ => throw new InvalidDataException($"Unknown NRRD type: {dataType}.")
  };

  private static string[] _ParseLabels(string value) {
    var result = new System.Collections.Generic.List<string>();
    var i = 0;
    while (i < value.Length) {
      if (value[i] == '"') {
        var end = value.IndexOf('"', i + 1);
        if (end < 0)
          break;

        result.Add(value.Substring(i + 1, end - i - 1));
        i = end + 1;
      } else
        ++i;
    }

    return result.ToArray();
  }
}
