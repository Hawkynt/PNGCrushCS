using System;
using System.IO;

namespace FileFormat.Lss16;

/// <summary>Reads Syslinux LSS16 files from bytes, streams, or file paths.</summary>
public static class Lss16Reader {

  public static Lss16File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LSS16 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Lss16File FromStream(Stream stream) {
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

  public static Lss16File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Lss16File.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid LSS16 file: expected at least {Lss16File.HeaderSize} bytes, got {data.Length}.");

    if (data[0] != Lss16File.Magic[0] || data[1] != Lss16File.Magic[1] || data[2] != Lss16File.Magic[2] || data[3] != Lss16File.Magic[3])
      throw new InvalidDataException($"Invalid LSS16 magic: expected 0x{Lss16File.Magic[0]:X2} 0x{Lss16File.Magic[1]:X2} 0x{Lss16File.Magic[2]:X2} 0x{Lss16File.Magic[3]:X2}.");

    var width = (int)(data[4] | (data[5] << 8));
    var height = (int)(data[6] | (data[7] << 8));

    if (width == 0)
      throw new InvalidDataException("Invalid LSS16 width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid LSS16 height: must be greater than zero.");

    var palette = new byte[Lss16File.PaletteSize];
    data.Slice(8, Lss16File.PaletteSize).CopyTo(palette.AsSpan(0));

    // _DecodePixels uses a local function that captures the buffer, which cannot be a span
    var pixelData = _DecodePixels(data.ToArray(), Lss16File.HeaderSize, width, height);

    return new Lss16File {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
    };
  
  }

  public static Lss16File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Decodes nybble-based RLE pixel data from the LSS16 stream.</summary>
  private static byte[] _DecodePixels(byte[] data, int offset, int width, int height) {
    var pixels = new byte[width * height];
    var bytePos = offset;
    var highNybble = false;
    var currentByte = (byte)0;

    byte ReadNybble() {
      if (!highNybble) {
        if (bytePos >= data.Length)
          return 0;

        currentByte = data[bytePos++];
        highNybble = true;
        return (byte)(currentByte & 0x0F);
      }

      highNybble = false;
      return (byte)((currentByte >> 4) & 0x0F);
    }

    for (var y = 0; y < height; ++y) {
      byte previousPixel = 0;
      highNybble = false;
      var x = 0;

      while (x < width) {
        var nybble = ReadNybble();

        if (nybble != previousPixel) {
          pixels[y * width + x] = nybble;
          previousPixel = nybble;
          ++x;
        } else {
          int runLength = ReadNybble();

          if (runLength == 0) {
            var highPart = ReadNybble();
            var lowPart = ReadNybble();
            runLength = ((highPart << 4) | lowPart) + 16;
          }

          for (var i = 0; i < runLength && x < width; ++i) {
            pixels[y * width + x] = previousPixel;
            ++x;
          }
        }
      }
    }

    return pixels;
  }
}
