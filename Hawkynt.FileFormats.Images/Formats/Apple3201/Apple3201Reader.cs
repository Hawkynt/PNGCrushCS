using System;
using System.IO;

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

  /// <summary>
  /// Unpacks Apple's PackBytes, whose command byte says both how many bytes follow and how far to
  /// step back between them.
  /// </summary>
  /// <remarks>
  /// The top two bits choose a stride of nothing, one, or four: nothing gives a run of literals,
  /// one gives a byte repeated, and four gives a four-byte pattern repeated — which is exactly what
  /// a dither or a run of identical pixels in a four-byte-aligned bitmap produces. The two long
  /// forms multiply the count by four, so a screen of one colour costs two bytes for every 256.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var unpacked = new byte[Apple3201File.Stride * Apple3201File.Height];
    var at = Apple3201File.BitmapOffset;
    var count = 1;
    var stride = 0;

    for (var target = 0; target < unpacked.Length; ++target) {
      if (--count == 0) {
        if (at >= data.Length)
          throw new InvalidDataException("A 3201 picture ends before its picture does.");

        var command = data[at++];
        count = (command & 63) + 1;
        if (command >= 128)
          count <<= 2;

        ReadOnlySpan<int> strides = [0, 1, 4, 1];
        stride = strides[command >> 6];
      } else if (stride != 0 && (count & (stride - 1)) == 0)
        at -= stride;

      if (at >= data.Length)
        throw new InvalidDataException("A 3201 picture's stream runs past the end of the file.");

      unpacked[target] = data[at++];
    }

    return unpacked;
  }

  public static Apple3201File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
