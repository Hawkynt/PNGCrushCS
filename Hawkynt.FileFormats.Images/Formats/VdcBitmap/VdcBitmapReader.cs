using System;
using System.IO;

namespace FileFormat.VdcBitmap;

/// <summary>Reads VDC BitMaps from bytes, streams, or file paths.</summary>
public static class VdcBitmapReader {

  public static VdcBitmapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Bitmap not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VdcBitmapFile FromStream(Stream stream) {
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

  public static VdcBitmapFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 9 || !data[..VdcBitmapFile.Signature.Length].SequenceEqual(VdcBitmapFile.Signature))
      throw new InvalidDataException("Not a VDC BitMap.");

    var width = (data[4] << 8) | data[5];
    var height = (data[6] << 8) | data[7];
    var stride = (width + 7) >> 3;
    if (width == 0 || height == 0)
      throw new InvalidDataException($"A VDC BitMap is not {width}x{height}.");

    switch (data[3]) {
      case 2: {
        if (data.Length != VdcBitmapFile.Version2BitmapOffset + stride * height)
          throw new InvalidDataException($"A {width}x{height} version 2 bitmap does not fit {data.Length} bytes.");

        return new() {
          Bitmap = data[VdcBitmapFile.Version2BitmapOffset..].ToArray(),
          Width = width,
          Height = height,
          InkIsBlack = true,
        };
      }

      case 3: {
        if (data.Length < 19)
          throw new InvalidDataException("A version 3 bitmap is at least nineteen bytes.");

        // The header carries a comment of its own length before the picture starts.
        var offset = 18 + (data[16] << 8) + data[17];
        if (offset > data.Length)
          throw new InvalidDataException("A version 3 bitmap's comment runs past the end of the file.");

        var bitmap = data[8] == 0
          ? _Raw(data, offset, stride, height)
          : _Unpack(data, offset, stride, height);

        return new() { Bitmap = bitmap, Width = width, Height = height, InkIsBlack = false };
      }

      default:
        throw new InvalidDataException($"A VDC BitMap is version 2 or 3, not {data[3]}.");
    }
  }

  private static byte[] _Raw(ReadOnlySpan<byte> data, int offset, int stride, int height) {
    if (data.Length != offset + stride * height)
      throw new InvalidDataException($"An unpacked version 3 bitmap does not fit {data.Length} bytes.");

    return data[offset..].ToArray();
  }

  /// <summary>
  /// Unpacks the run-length encoding, whose five escape bytes the file chooses for itself.
  /// </summary>
  /// <remarks>
  /// Three take a run length from the stream — one with the value following, and two for the runs
  /// of all-clear and all-set bytes a monochrome picture is mostly made of. The other two are those
  /// same two values repeated exactly twice, which is short enough to be worth a byte of its own.
  /// Any byte that is not one of the five stands for itself.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data, int offset, int stride, int height) {
    var bitmap = new byte[stride * height];
    var at = offset;

    for (var i = 0; i < bitmap.Length;) {
      if (at >= data.Length)
        throw new InvalidDataException("A version 3 bitmap's packed stream ends early.");

      var command = data[at++];
      byte value;
      int count;

      if (command == data[VdcBitmapFile.EscapeOffset]) {
        if (at + 1 >= data.Length)
          throw new InvalidDataException("A version 3 bitmap's packed stream ends early.");

        value = data[at++];
        count = data[at++];
      } else if (command == data[VdcBitmapFile.EscapeOffset + 1] || command == data[VdcBitmapFile.EscapeOffset + 2]) {
        if (at >= data.Length)
          throw new InvalidDataException("A version 3 bitmap's packed stream ends early.");

        value = command == data[VdcBitmapFile.EscapeOffset + 1] ? (byte)0 : (byte)255;
        count = data[at++];
      } else if (command == data[VdcBitmapFile.EscapeOffset + 3] || command == data[VdcBitmapFile.EscapeOffset + 4]) {
        value = command == data[VdcBitmapFile.EscapeOffset + 3] ? (byte)0 : (byte)255;
        count = 2;
      } else {
        value = command;
        count = 1;
      }

      if (count == 0)
        throw new InvalidDataException("A version 3 bitmap declares a run of nothing.");

      while (count-- > 0 && i < bitmap.Length)
        bitmap[i++] = value;
    }

    return bitmap;
  }

  public static VdcBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
