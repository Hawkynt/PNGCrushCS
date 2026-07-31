using System;
using System.IO;
using System.Text;

namespace FileFormat.PaintShopCompressed;

/// <summary>Reads compressed PaintShop pictures from bytes, streams, or file paths.</summary>
public static class PaintShopCompressedReader {

  /// <summary>Fills a line with black.</summary>
  private const byte _FILL_CLEAR = 0;

  /// <summary>Repeats the line above between one and 256 times.</summary>
  private const byte _REPEAT_SHORT = 10;

  /// <summary>Repeats the line above between 257 and 512 times.</summary>
  private const byte _REPEAT_LONG = 12;

  /// <summary>Fills a line with one byte.</summary>
  private const byte _FILL_BYTE = 100;

  /// <summary>Fills a line with two bytes alternating.</summary>
  private const byte _FILL_PAIR = 102;

  /// <summary>Stores a line as it is.</summary>
  private const byte _LITERAL = 110;

  /// <summary>Fills a line with white.</summary>
  private const byte _FILL_SET = 200;

  /// <summary>The command byte a file uses when it stores no commands at all.</summary>
  private const byte _UNCOMPRESSED = 99;

  public static PaintShopCompressedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PaintShopCompressedFile FromStream(Stream stream) {
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

  public static PaintShopCompressedFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 18
        || Encoding.ASCII.GetString(data[..PaintShopCompressedFile.Signature.Length]) != PaintShopCompressedFile.Signature
        || data[8] != 2 || data[9] != 1)
      throw new InvalidDataException("Not a compressed PaintShop picture.");

    // The stored dimensions are one less than the real ones, so 640 and 400 still fit two bytes.
    var width = ((data[10] << 8) + data[11]) + 1;
    var height = ((data[12] << 8) + data[13]) + 1;
    if (width > 640 || height > 400)
      throw new InvalidDataException($"A PaintShop picture is not {width}x{height}.");

    var stride = (width + 7) >> 3;
    var length = stride * height;

    // A file may hold the bitmap outright, marked by a command that then covers the whole picture.
    if (data[PaintShopCompressedFile.CommandsOffset] == _UNCOMPRESSED && data.Length == 16 + length
        && data[15 + length] == PaintShopCompressedFile.Terminator)
      return new() { Bitmap = data.Slice(15, length).ToArray(), Width = width, Height = height };

    return new() { Bitmap = _Unpack(data, stride, length), Width = width, Height = height };
  }

  private static byte[] _Unpack(ReadOnlySpan<byte> data, int stride, int length) {
    var bitmap = new byte[length];
    var at = PaintShopCompressedFile.CommandsOffset;

    for (var target = 0; target < length;) {
      if (at + 1 >= data.Length)
        throw new InvalidDataException("A PaintShop picture's commands end before its picture does.");

      var command = data[at++];
      switch (command) {
        case _FILL_CLEAR:
        case _FILL_SET:
          bitmap.AsSpan(target, stride).Fill(command == _FILL_SET ? (byte)255 : (byte)0);
          target += stride;
          break;

        case _FILL_BYTE:
          bitmap.AsSpan(target, stride).Fill(data[at++]);
          target += stride;
          break;

        case _FILL_PAIR:
          if (at + 2 >= data.Length)
            throw new InvalidDataException("A PaintShop picture's alternating fill has no bytes.");

          for (var i = 0; i < stride; ++i)
            bitmap[target + i] = data[at + (i & 1)];

          at += 2;
          target += stride;
          break;

        case _LITERAL:
          if (at + stride >= data.Length)
            throw new InvalidDataException("A PaintShop picture's literal line runs past the end.");

          data.Slice(at, stride).CopyTo(bitmap.AsSpan(target));
          at += stride;
          target += stride;
          break;

        case _REPEAT_SHORT:
        case _REPEAT_LONG: {
          var count = (command == _REPEAT_SHORT ? 1 : 257) + data[at++];
          if (target < stride || target + count * stride > length)
            throw new InvalidDataException("A PaintShop picture repeats a line it does not have.");

          do {
            bitmap.AsSpan(target - stride, stride).CopyTo(bitmap.AsSpan(target));
            target += stride;
          } while (--count > 0);

          break;
        }

        default:
          throw new InvalidDataException($"Not a compressed PaintShop picture: a command of {command}.");
      }
    }

    if (at >= data.Length || data[at] != PaintShopCompressedFile.Terminator)
      throw new InvalidDataException("A PaintShop picture's commands are not closed.");

    return bitmap;
  }

  public static PaintShopCompressedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
