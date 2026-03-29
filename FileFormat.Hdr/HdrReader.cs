using System;
using System.IO;

namespace FileFormat.Hdr;

/// <summary>Reads Radiance HDR files from bytes, streams, or file paths.</summary>
public static class HdrReader {

  private const int _MIN_FILE_SIZE = 11; // "#?RADIANCE\n" minimum

  public static HdrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HDR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HdrFile FromStream(Stream stream) {
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

  public static HdrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data is too small to be a valid HDR file.");

    if (data[0] != (byte)'#' || data[1] != (byte)'?')
      throw new InvalidDataException("Invalid HDR magic: expected '#?'.");

    var (width, height, exposure, dataOffset) = HdrHeaderParser.Parse(data);
    var pixelData = _DecodeScanlines(data, dataOffset, width, height);

    return new HdrFile {
      Width = width,
      Height = height,
      Exposure = exposure,
      PixelData = pixelData
    };
  }

  private static float[] _DecodeScanlines(byte[] data, int offset, int width, int height) {
    var pixels = new float[width * height * 3];
    var pos = offset;

    for (var y = 0; y < height; ++y) {
      if (_IsAdaptiveRle(data, pos, width)) {
        pos = _DecodeAdaptiveRleScanline(data, pos, width, pixels, y * width * 3);
      } else {
        pos = _DecodeOldStyleScanline(data, pos, width, pixels, y * width * 3);
      }
    }

    return pixels;
  }

  private static bool _IsAdaptiveRle(byte[] data, int pos, int width) {
    if (width < 8 || width > 0x7FFF)
      return false;

    if (pos + 4 > data.Length)
      return false;

    return data[pos] == 2 && data[pos + 1] == 2 && data[pos + 2] == (byte)(width >> 8) && data[pos + 3] == (byte)(width & 0xFF);
  }

  private static int _DecodeAdaptiveRleScanline(byte[] data, int pos, int width, float[] pixels, int pixelOffset) {
    // Skip marker bytes
    pos += 4;

    var scanline = new byte[width * 4];

    // Read 4 channels separately
    for (var ch = 0; ch < 4; ++ch) {
      var i = 0;
      while (i < width) {
        if (pos >= data.Length)
          throw new InvalidDataException("Unexpected end of data in adaptive RLE scanline.");

        var count = data[pos++];
        if (count > 128) {
          // Run
          var runLength = count - 128;
          if (pos >= data.Length)
            throw new InvalidDataException("Unexpected end of data in adaptive RLE run.");

          var value = data[pos++];
          for (var j = 0; j < runLength && i < width; ++j)
            scanline[i++ * 4 + ch] = value;
        } else {
          // Literal
          for (var j = 0; j < count && i < width; ++j) {
            if (pos >= data.Length)
              throw new InvalidDataException("Unexpected end of data in adaptive RLE literal.");

            scanline[i++ * 4 + ch] = data[pos++];
          }
        }
      }
    }

    // Convert RGBE to float
    for (var x = 0; x < width; ++x) {
      var (r, g, b) = RgbeCodec.DecodePixel(
        scanline[x * 4],
        scanline[x * 4 + 1],
        scanline[x * 4 + 2],
        scanline[x * 4 + 3]
      );
      pixels[pixelOffset + x * 3] = r;
      pixels[pixelOffset + x * 3 + 1] = g;
      pixels[pixelOffset + x * 3 + 2] = b;
    }

    return pos;
  }

  private static int _DecodeOldStyleScanline(byte[] data, int pos, int width, float[] pixels, int pixelOffset) {
    for (var x = 0; x < width; ++x) {
      if (pos + 4 > data.Length)
        throw new InvalidDataException("Unexpected end of data in old-style scanline.");

      var (r, g, b) = RgbeCodec.DecodePixel(data[pos], data[pos + 1], data[pos + 2], data[pos + 3]);
      pixels[pixelOffset + x * 3] = r;
      pixels[pixelOffset + x * 3 + 1] = g;
      pixels[pixelOffset + x * 3 + 2] = b;
      pos += 4;
    }

    return pos;
  }
}
