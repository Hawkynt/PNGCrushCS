using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Netpbm;

/// <summary>Reads Netpbm files (PBM/PGM/PPM/PAM) from bytes, streams, or file paths.</summary>
public static class NetpbmReader {

  private const int _MIN_SIZE = 7; // "P5\n1 1\n1\n" minimal valid (shorter exists but this covers sanity)

  public static NetpbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Netpbm file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NetpbmFile FromStream(Stream stream) {
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

  public static NetpbmFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static NetpbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException("Data too small for a valid Netpbm file.");

    if (data[0] != (byte)'P' || data[1] < (byte)'1' || data[1] > (byte)'7')
      throw new InvalidDataException("Invalid Netpbm magic number.");

    var header = NetpbmHeaderParser.Parse(data);

    var pixelData = header.Format switch {
      NetpbmFormat.PbmAscii => _ReadPbmAscii(data, header),
      NetpbmFormat.PgmAscii => _ReadAsciiSamples(data, header),
      NetpbmFormat.PpmAscii => _ReadAsciiSamples(data, header),
      NetpbmFormat.PbmBinary => _ReadPbmBinary(data, header),
      NetpbmFormat.PgmBinary or NetpbmFormat.PpmBinary or NetpbmFormat.Pam => _ReadBinarySamples(data, header),
      _ => throw new InvalidDataException($"Unsupported Netpbm format: {header.Format}.")
    };

    return new NetpbmFile {
      Format = header.Format,
      Width = header.Width,
      Height = header.Height,
      MaxValue = header.MaxValue,
      Channels = header.Channels,
      PixelData = pixelData,
      TupleType = header.TupleType
    };
  }

  private static byte[] _ReadPbmAscii(byte[] data, NetpbmHeaderParser.ParsedHeader header) {
    var totalPixels = header.Width * header.Height;
    var result = new byte[totalPixels];
    var offset = header.DataOffset;
    var idx = 0;

    while (idx < totalPixels && offset < data.Length) {
      var b = data[offset];
      ++offset;

      if (b == (byte)'0')
        result[idx++] = 0;
      else if (b == (byte)'1')
        result[idx++] = 1;
      // skip whitespace and other chars
    }

    return result;
  }

  private static byte[] _ReadAsciiSamples(byte[] data, NetpbmHeaderParser.ParsedHeader header) {
    var totalSamples = header.Width * header.Height * header.Channels;
    var bytesPerSample = header.MaxValue > 255 ? 2 : 1;
    var result = new byte[totalSamples * bytesPerSample];
    var offset = header.DataOffset;
    var idx = 0;

    while (idx < totalSamples && offset < data.Length) {
      // skip whitespace and comments
      while (offset < data.Length && (data[offset] == (byte)' ' || data[offset] == (byte)'\t' || data[offset] == (byte)'\n' || data[offset] == (byte)'\r'))
        ++offset;

      if (offset < data.Length && data[offset] == (byte)'#') {
        while (offset < data.Length && data[offset] != (byte)'\n')
          ++offset;

        continue;
      }

      if (offset >= data.Length)
        break;

      // read decimal value
      var start = offset;
      while (offset < data.Length && data[offset] >= (byte)'0' && data[offset] <= (byte)'9')
        ++offset;

      if (offset > start) {
        var value = int.Parse(Encoding.ASCII.GetString(data, start, offset - start));
        if (bytesPerSample == 2) {
          result[idx * 2] = (byte)(value >> 8);
          result[idx * 2 + 1] = (byte)(value & 0xFF);
        } else
          result[idx] = (byte)value;

        ++idx;
      }
    }

    return result;
  }

  private static byte[] _ReadPbmBinary(byte[] data, NetpbmHeaderParser.ParsedHeader header) {
    var totalPixels = header.Width * header.Height;
    var result = new byte[totalPixels];
    var bytesPerRow = (header.Width + 7) / 8;
    var offset = header.DataOffset;
    var pixelIdx = 0;

    for (var row = 0; row < header.Height && offset < data.Length; ++row) {
      for (var byteIdx = 0; byteIdx < bytesPerRow && offset < data.Length; ++byteIdx) {
        var packed = data[offset++];
        for (var bit = 7; bit >= 0 && pixelIdx < (row + 1) * header.Width; --bit)
          result[pixelIdx++] = (byte)((packed >> bit) & 1);
      }
    }

    return result;
  }

  private static byte[] _ReadBinarySamples(byte[] data, NetpbmHeaderParser.ParsedHeader header) {
    var totalSamples = header.Width * header.Height * header.Channels;
    var bytesPerSample = header.MaxValue > 255 ? 2 : 1;
    var totalBytes = totalSamples * bytesPerSample;
    var available = Math.Min(totalBytes, data.Length - header.DataOffset);
    var result = new byte[totalBytes];
    data.AsSpan(header.DataOffset, available).CopyTo(result.AsSpan(0));
    return result;
  }
}
