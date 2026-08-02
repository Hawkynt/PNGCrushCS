using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AwardBmp;

/// <summary>Reads Award BIOS bitmap logos (AWBM) from bytes, streams, or file paths.</summary>
public static class AwardBmpReader {

  public static AwardBmpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Award BIOS bitmap not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AwardBmpFile FromStream(Stream stream) {
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

  public static AwardBmpFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 8 || !data[..4].SequenceEqual(AwardBmpFile.Signature))
      throw new InvalidDataException("Not an Award BIOS bitmap: the file does not begin with AWBM.");

    int width = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    int height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"An Award BIOS bitmap states a size of {width} by {height}.");

    var stride = AwardBmpFile.StrideOf(width);
    var planeBytes = stride * AwardBmpFile.Planes * height;
    if (data.Length < 8 + planeBytes)
      throw new InvalidDataException($"{width} by {height} needs {planeBytes} bytes of picture; the file has {data.Length - 8}.");

    // Four bitplanes, interleaved a row at a time, each contributing one bit of the colour index.
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var row = 8 + y * stride * AwardBmpFile.Planes;
      for (var plane = 0; plane < AwardBmpFile.Planes; ++plane) {
        var from = row + plane * stride;
        for (var x = 0; x < width; ++x)
          if ((data[from + (x >> 3)] >> (~x & 7) & 1) != 0)
            pixels[y * width + x] |= (byte)(1 << plane);
      }
    }

    // The palette sits at the end behind its own marker, six bits a channel.
    var palette = new byte[AwardBmpFile.PaletteCount * 3];
    var at = 8 + planeBytes;
    if (at + AwardBmpFile.PaletteMarker.Length + palette.Length <= data.Length
        && data.Slice(at, AwardBmpFile.PaletteMarker.Length).SequenceEqual(AwardBmpFile.PaletteMarker)) {
      var from = at + AwardBmpFile.PaletteMarker.Length;
      for (var i = 0; i < palette.Length; ++i) {
        var six = data[from + i] & 0x3F;
        palette[i] = (byte)(six << 2 | six >> 4);
      }
    } else
      throw new InvalidDataException("An Award BIOS bitmap ends with an RGB palette; this file does not.");

    return new() {
      Width = width,
      Height = height,
      PixelData = pixels,
      Palette = palette,
    };
  }

  public static AwardBmpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
