using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Prisms;

/// <summary>Reads Prisms pictures from bytes, streams, or file paths.</summary>
public static class PrismsReader {

  public static PrismsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Prisms file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrismsFile FromStream(Stream stream) {
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

  public static PrismsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PrismsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PrismsFile.MinFileSize)
      throw new InvalidDataException(
        $"Data too small for a Prisms picture (at least {PrismsFile.MinFileSize} bytes are needed, got {data.Length}).");

    if (!data[..PrismsFile.Signature.Length].SequenceEqual(PrismsFile.Signature))
      throw new InvalidDataException("Not a Prisms picture: the four bytes it opens with are not the format's.");

    if (!data.Slice(PrismsFile.LayoutOffset, PrismsFile.Layout.Length).SequenceEqual(PrismsFile.Layout))
      throw new InvalidDataException("Not a Prisms picture: the eight characters at 0x86 do not name the pixel layout.");

    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[PrismsFile.HeightOffset..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[PrismsFile.WidthOffset..]);
    if (width < 1 || height < 1)
      throw new InvalidDataException($"A Prisms picture states a size of {width}x{height}.");

    var start = BinaryPrimitives.ReadUInt16LittleEndian(data[PrismsFile.DataPointerOffset..]);
    if (start < PrismsFile.MinFileSize || start >= data.Length)
      throw new InvalidDataException(
        $"A Prisms picture says its coding begins at {start}, which is not inside a file of {data.Length} bytes.");

    return new() { Width = width, Height = height, PixelData = _Decode(data, start, width, height) };
  }

  /// <summary>Runs the command stream, filling rows from the bottom of the picture upwards.</summary>
  private static byte[] _Decode(ReadOnlySpan<byte> data, int start, int width, int height) {
    var pixels = new byte[width * height * 3];
    var row = new byte[width * 4];
    var at = start;
    var x = 0;
    var y = 0;

    while (y < height) {
      if (at + 2 > data.Length)
        throw new InvalidDataException(
          $"A Prisms picture's coding runs out after {y} of the {height} rows its header states.");

      var count = data[at];
      var opcode = data[at + 1];
      at += 2;

      switch (opcode) {
        case PrismsFile.OpcodeLiteral: {
          var run = count + 1;
          var bytes = run * 4;
          if (at + bytes > data.Length)
            throw new InvalidDataException("A Prisms picture's literal run reaches past the end of the file.");

          _Place(data.Slice(at, bytes), row, ref x, run, width);
          at += bytes;
          if (x >= width)
            _Emit(row, pixels, width, height, ref x, ref y);

          break;
        }

        case PrismsFile.OpcodeRuns: {
          for (var group = 0; group <= count && y < height; ++group) {
            if (at + 5 > data.Length)
              throw new InvalidDataException("A Prisms picture's run-length group reaches past the end of the file.");

            var run = data[at] + 1;
            var pixel = data.Slice(at + 1, 4);
            at += 5;

            for (var i = 0; i < run && x < width; ++i, ++x)
              pixel.CopyTo(row.AsSpan(x * 4));

            if (x >= width)
              _Emit(row, pixels, width, height, ref x, ref y);
          }

          break;
        }

        case PrismsFile.OpcodeAlign when count == 0: {
          var over = at % PrismsFile.AlignTo;
          if (over != 0)
            at += PrismsFile.AlignTo - over;

          break;
        }

        // Every other opcode is a command the converter reads and does nothing with.
      }
    }

    return pixels;
  }

  private static void _Place(ReadOnlySpan<byte> source, byte[] row, ref int x, int run, int width) {
    for (var i = 0; i < run && x < width; ++i, ++x)
      source.Slice(i * 4, 4).CopyTo(row.AsSpan(x * 4));
  }

  /// <summary>Hands a finished row over, bottom row first.</summary>
  private static void _Emit(byte[] row, byte[] pixels, int width, int height, ref int x, ref int y) {
    var target = (height - 1 - y) * width * 3;
    for (var i = 0; i < width; ++i) {
      // The header calls the pixel R8G8B8A8, but what the converter draws is the fourth byte as red,
      // the third as green and the second as blue; the first is not drawn.
      pixels[target + i * 3] = row[i * 4 + 3];
      pixels[target + i * 3 + 1] = row[i * 4 + 2];
      pixels[target + i * 3 + 2] = row[i * 4 + 1];
    }

    Array.Clear(row);
    x = 0;
    ++y;
  }
}
