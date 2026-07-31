using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Apple3201;

/// <summary>Reads 3201 pictures from bytes, streams, or file paths.</summary>
public static class Apple3201Reader {

  public static Apple3201File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Apple3201File FromStream(Stream stream) {
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

  public static Apple3201File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 6654 || !data[..Apple3201File.Signature.Length].SequenceEqual(Apple3201File.Signature))
      throw new InvalidDataException("Not a 3201 picture.");

    return new() { Data = data.ToArray(), Bitmap = _Unpack(data) };
  }

  /// <summary>Unpacks the whole bitmap, which is one PackBytes stream from end to end.</summary>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var unpacked = new byte[Apple3201File.Stride * Apple3201File.Height];
    var stream = new PackBytesStream(Apple3201File.BitmapOffset);

    for (var target = 0; target < unpacked.Length; ++target) {
      var value = stream.ReadByte(data);
      if (value < 0)
        throw new InvalidDataException("A 3201 picture ends before its picture does.");

      unpacked[target] = (byte)value;
    }

    return unpacked;
  }

  public static Apple3201File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
