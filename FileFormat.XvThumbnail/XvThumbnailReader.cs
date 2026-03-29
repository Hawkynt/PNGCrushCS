using System;
using System.IO;
using System.Text;

namespace FileFormat.XvThumbnail;

/// <summary>Reads XV thumbnail files from bytes, streams, or file paths.</summary>
public static class XvThumbnailReader {

  /// <summary>The magic header bytes for XV thumbnail format.</summary>
  private static readonly byte[] _MAGIC = "P7 332\n"u8.ToArray();

  public static XvThumbnailFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XV thumbnail file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XvThumbnailFile FromStream(Stream stream) {
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

  public static XvThumbnailFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MAGIC.Length)
      throw new InvalidDataException($"Data too small for XV thumbnail: need at least {_MAGIC.Length} bytes, got {data.Length}.");

    for (var i = 0; i < _MAGIC.Length; ++i)
      if (data[i] != _MAGIC[i])
        throw new InvalidDataException("Invalid XV thumbnail magic: expected \"P7 332\n\".");

    var offset = _MAGIC.Length;

    // Skip comment lines starting with '#'
    while (offset < data.Length && data[offset] == (byte)'#') {
      var eol = Array.IndexOf(data, (byte)'\n', offset);
      if (eol < 0)
        throw new InvalidDataException("Unterminated comment line in XV thumbnail header.");
      offset = eol + 1;
    }

    // Parse "width height maxval\n"
    var newlineIndex = Array.IndexOf(data, (byte)'\n', offset);
    if (newlineIndex < 0)
      throw new InvalidDataException("No dimension line found in XV thumbnail header.");

    var dimLine = Encoding.ASCII.GetString(data, offset, newlineIndex - offset);
    var parts = dimLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 3)
      throw new InvalidDataException($"Expected \"width height maxval\" but got \"{dimLine}\".");

    if (!int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height) || !int.TryParse(parts[2], out _))
      throw new InvalidDataException($"Invalid dimensions in XV thumbnail header: \"{dimLine}\".");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("XV thumbnail dimensions must be positive.");

    var pixelOffset = newlineIndex + 1;
    var expectedPixelBytes = width * height;
    var available = data.Length - pixelOffset;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(pixelOffset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new XvThumbnailFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
