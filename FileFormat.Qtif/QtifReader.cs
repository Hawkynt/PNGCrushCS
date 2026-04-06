using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Qtif;

/// <summary>Reads QTIF (QuickTime Image) files from bytes, streams, or file paths.</summary>
public static class QtifReader {

  /// <summary>Minimum size: one atom header (8 bytes).</summary>
  private const int _MIN_SIZE = 8;

  /// <summary>Size of the fixed image description structure.</summary>
  private const int _IDSC_SIZE = 86;

  public static QtifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("QTIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static QtifFile FromStream(Stream stream) {
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

  public static QtifFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static QtifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException($"Data too small for QTIF: expected at least {_MIN_SIZE} bytes, got {data.Length}.");

    int width = 0, height = 0;
    byte[]? pixelData = null;
    var hasIdsc = false;

    var offset = 0;
    while (offset + 8 <= data.Length) {
      var atomSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
      var atomType = Encoding.ASCII.GetString(data, offset + 4, 4);

      if (atomSize < 8)
        throw new InvalidDataException($"Invalid atom size {atomSize} at offset {offset}.");

      if (offset + atomSize > data.Length)
        break;

      switch (atomType) {
        case "idsc":
          if (atomSize - 8 < _IDSC_SIZE)
            throw new InvalidDataException($"Image description too small: expected at least {_IDSC_SIZE} bytes, got {atomSize - 8}.");
          width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 8 + 32));
          height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 8 + 34));
          hasIdsc = true;
          break;
        case "idat":
          var dataLen = atomSize - 8;
          pixelData = new byte[dataLen];
          data.AsSpan(offset + 8, dataLen).CopyTo(pixelData.AsSpan(0));
          break;
      }

      offset += atomSize;
    }

    if (pixelData == null)
      throw new InvalidDataException("No 'idat' atom found in QTIF data.");

    if (!hasIdsc) {
      var totalPixels = pixelData.Length / 3;
      var side = (int)Math.Sqrt(totalPixels);
      if (side * side * 3 == pixelData.Length) {
        width = side;
        height = side;
      } else
        throw new InvalidDataException("No 'idsc' atom found and cannot infer dimensions from 'idat' size.");
    }

    return new QtifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
