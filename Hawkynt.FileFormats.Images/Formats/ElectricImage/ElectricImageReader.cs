using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.ElectricImage;

/// <summary>Reads ElectricImage pictures (.ei, .eidi) from bytes, streams, or file paths.</summary>
public static class ElectricImageReader {

  /// <summary>The mode that carries five bytes of its own behind the frame header.</summary>
  private const int _ModeWithExtraFive = 0x0001;

  /// <summary>The mode the eight-bit frames carry, which has nothing behind the palette.</summary>
  private const int _ModePlain = 0x0100;

  public static ElectricImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ElectricImage file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ElectricImageFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static ElectricImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ElectricImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ElectricImageFile.FileHeaderSize + ElectricImageFile.FrameHeaderSize)
      throw new InvalidDataException($"Data too small for an ElectricImage picture (got {data.Length} bytes).");

    var version = _Read16(data, 0);
    if (version != ElectricImageFile.Version)
      throw new InvalidDataException($"Not an ElectricImage picture this reads: it states version {version} and only {ElectricImageFile.Version} was ever seen.");

    var frameCount = _Read32(data, 2);
    if (frameCount is < 1 or > 4096)
      throw new InvalidDataException($"An ElectricImage file of {frameCount} frames is not one this reads.");

    var frames = new List<ElectricImageFile.Frame>();
    var at = ElectricImageFile.FileHeaderSize;

    for (var index = 0; index < frameCount; ++index) {
      if (at + ElectricImageFile.FrameHeaderSize > data.Length)
        throw new InvalidDataException($"Frame {index} of {frameCount} starts at {at}, past the end of a file of {data.Length} bytes.");

      var height = _Read16(data, at + 8);
      var width = _Read16(data, at + 10);
      var depth = data[at + 12];
      var extra = _Read16(data, at + 22);
      var dataSize = _Read32(data, at + 24);
      var mode = _Read16(data, at + 28);
      at += ElectricImageFile.FrameHeaderSize;

      if (width == 0 || height == 0)
        throw new InvalidDataException($"An ElectricImage frame of {width}x{height} has no picture in it.");

      byte[]? palette = null;
      if (depth is 1 or 8) {
        if (at + 2 > data.Length)
          throw new InvalidDataException("The frame's colour table runs past the end of the file.");

        var first = data[at];
        var last = data[at + 1];
        at += 2;
        if (last < first)
          throw new InvalidDataException($"The frame's colour table runs from {first} to {last}, which is backwards.");

        var entries = last - first + 1;
        if (at + entries * 3 > data.Length)
          throw new InvalidDataException("The frame's colour table runs past the end of the file.");

        palette = new byte[(last + 1) * 3];
        data.Slice(at, entries * 3).CopyTo(palette.AsSpan(first * 3));
        at += entries * 3;
      }

      at += mode switch {
        _ModeWithExtraFive => 5,
        _ModePlain => 0,
        _ => throw new InvalidDataException($"An ElectricImage frame in mode 0x{mode:X4} is not one this reads; 0x0001 and 0x0100 are."),
      };

      var bytesPerPixel = depth switch {
        8 => 1,
        24 when (extra & 0xFF) == 8 => 4,
        24 => 3,
        32 => 4,
        _ => throw new InvalidDataException($"An ElectricImage frame at {depth} bits is not one this reads; 8, 24 and 32 are."),
      };

      if (at + dataSize > data.Length)
        throw new InvalidDataException($"The frame states {dataSize} bytes of picture at {at}, which is past the end of a file of {data.Length} bytes.");

      var pixels = _Unpack(data.Slice(at, (int)dataSize), bytesPerPixel, (long)width * height);
      at += (int)dataSize;

      frames.Add(new() {
        Width = width,
        Height = height,
        BytesPerPixel = bytesPerPixel,
        PixelData = pixels,
        Palette = palette,
      });
    }

    // Every one of the eighteen files measured ends exactly where its last frame does. Requiring it
    // is what tells a picture from a file that merely opens with a plausible five.
    if (at != data.Length)
      throw new InvalidDataException($"The frames account for {at} bytes of a file of {data.Length}, so this is not an ElectricImage picture.");

    return new() { Frames = frames };
  }

  /// <summary>Unpacks one frame's run-length data, which has to come out at exactly the stated size.</summary>
  private static byte[] _Unpack(ReadOnlySpan<byte> data, int bytesPerPixel, long pixels) {
    var wanted = pixels * bytesPerPixel;
    if (wanted > int.MaxValue)
      throw new InvalidDataException("The frame is too large to unpack.");

    var output = new byte[wanted];
    var written = 0;
    var at = 0;

    while (at < data.Length) {
      var lead = data[at++];
      if (lead < 0x80) {
        var run = lead + 1;
        if (at + bytesPerPixel > data.Length || written + run * bytesPerPixel > wanted)
          throw new InvalidDataException("The frame's run-length data runs past what the frame states.");

        for (var i = 0; i < run; ++i) {
          data.Slice(at, bytesPerPixel).CopyTo(output.AsSpan(written));
          written += bytesPerPixel;
        }

        at += bytesPerPixel;
        continue;
      }

      var count = (lead & 0x7F) + 1;
      var bytes = count * bytesPerPixel;
      if (at + bytes > data.Length || written + bytes > wanted)
        throw new InvalidDataException("The frame's run-length data runs past what the frame states.");

      data.Slice(at, bytes).CopyTo(output.AsSpan(written));
      written += bytes;
      at += bytes;
    }

    if (written != wanted)
      throw new InvalidDataException($"The frame's run-length data came to {written} bytes where the size it states needs {wanted}.");

    return output;
  }

  private static int _Read16(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];

  private static uint _Read32(ReadOnlySpan<byte> data, int at)
    => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
}
