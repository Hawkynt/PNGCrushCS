using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.MetaImage;

/// <summary>Reads MetaImage (.mha) files from bytes, streams, or file paths.</summary>
public static class MetaImageReader {

  private const int _MIN_HEADER_SIZE = 20; // rough minimum for a valid header

  public static MetaImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MetaImage file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MetaImageFile FromStream(Stream stream) {
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

  public static MetaImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid MetaImage file.");

    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var dataOffset = _ParseHeader(data, headers);

    if (!headers.ContainsKey("ObjectType"))
      throw new InvalidDataException("Invalid MetaImage header: missing 'ObjectType' tag.");

    var width = 0;
    var height = 0;
    if (headers.TryGetValue("DimSize", out var dimSize)) {
      var parts = dimSize.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length >= 2) {
        width = int.Parse(parts[0]);
        height = int.Parse(parts[1]);
      }
    }

    var elementType = MetaImageElementType.MetUChar;
    if (headers.TryGetValue("ElementType", out var etStr))
      elementType = _ParseElementType(etStr);

    var channels = 1;
    if (headers.TryGetValue("ElementNumberOfChannels", out var chStr))
      channels = int.Parse(chStr);

    var isCompressed = false;
    if (headers.TryGetValue("CompressedData", out var compStr))
      isCompressed = compStr.Equals("True", StringComparison.OrdinalIgnoreCase);

    var bytesPerSample = _BytesPerSample(elementType);
    var expectedPixelBytes = width * height * channels * bytesPerSample;

    var remainingBytes = data.Length - dataOffset;
    byte[] pixelData;
    if (remainingBytes <= 0) {
      pixelData = [];
    } else if (isCompressed) {
      pixelData = _DecompressGzip(data, dataOffset, remainingBytes, expectedPixelBytes);
    } else {
      pixelData = new byte[expectedPixelBytes];
      data.AsSpan(dataOffset, Math.Min(remainingBytes, expectedPixelBytes)).CopyTo(pixelData.AsSpan(0));
    }

    return new MetaImageFile {
      Width = width,
      Height = height,
      ElementType = elementType,
      Channels = channels,
      IsCompressed = isCompressed,
      PixelData = pixelData,
    };
  }

  private static int _ParseHeader(byte[] data, Dictionary<string, string> headers) {
    var text = Encoding.ASCII.GetString(data);
    var pos = 0;

    while (pos < text.Length) {
      var lineEnd = text.IndexOf('\n', pos);
      if (lineEnd < 0)
        lineEnd = text.Length;

      var line = text.Substring(pos, lineEnd - pos).TrimEnd('\r');
      pos = lineEnd + 1;

      var eqIndex = line.IndexOf('=');
      if (eqIndex < 0)
        continue;

      var key = line.Substring(0, eqIndex).Trim();
      var value = line.Substring(eqIndex + 1).Trim();
      headers[key] = value;

      if (key.Equals("ElementDataFile", StringComparison.OrdinalIgnoreCase))
        return Encoding.ASCII.GetByteCount(text.Substring(0, pos));
    }

    return data.Length;
  }

  private static MetaImageElementType _ParseElementType(string value) => value.Trim() switch {
    "MET_UCHAR" => MetaImageElementType.MetUChar,
    "MET_SHORT" => MetaImageElementType.MetShort,
    "MET_USHORT" => MetaImageElementType.MetUShort,
    "MET_FLOAT" => MetaImageElementType.MetFloat,
    _ => throw new InvalidDataException($"Unsupported MetaImage element type: {value}."),
  };

  internal static int _BytesPerSample(MetaImageElementType type) => type switch {
    MetaImageElementType.MetUChar => 1,
    MetaImageElementType.MetShort => 2,
    MetaImageElementType.MetUShort => 2,
    MetaImageElementType.MetFloat => 4,
    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
  };

  private static byte[] _DecompressGzip(byte[] data, int offset, int length, int expectedSize) {
    using var inputStream = new MemoryStream(data, offset, length);
    using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
    using var outputStream = new MemoryStream();
    gzipStream.CopyTo(outputStream);
    var decompressed = outputStream.ToArray();

    var result = new byte[expectedSize];
    decompressed.AsSpan(0, Math.Min(decompressed.Length, expectedSize)).CopyTo(result.AsSpan(0));
    return result;
  }
}
