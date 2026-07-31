using System;
using System.IO;

namespace FileFormat.MsxScc;

/// <summary>Reads MSX2+ Screen 12 pictures from bytes, streams, or file paths.</summary>
public static class MsxSccReader {

  /// <summary>The leading byte of a BSAVE image stored as it stands.</summary>
  private const int _STORED = 254;

  /// <summary>The leading byte of one that is packed.</summary>
  private const int _PACKED = 253;

  /// <summary>Bytes a whole Screen 12 video memory occupies, header included.</summary>
  private const int _FULL_SIZE = 54279;

  /// <summary>The end address a file storing only the visible 192 rows declares.</summary>
  private const int _SHORT_END = 49151;

  public static MsxSccFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxSccFile FromStream(Stream stream) {
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

  public static MsxSccFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 7)
      throw new InvalidDataException("Not a Screen 12 picture: too short for a header.");

    // A file that declares exactly the visible screen stores 192 rows; anything else stores the
    // whole of video memory, which is 212 rows deep.
    var sprites = data.Length == MsxSccFile.WithSpritesSize && data[0] == _STORED ? data.ToArray() : [];

    if (data.Length >= 49159 && data[0] == _STORED && _EndAddress(data) == _SHORT_END)
      return new() { Screen = data.ToArray(), Height = 192, Sprites = sprites };

    return new() { Screen = _Unpack(data), Height = 212, Sprites = sprites };
  }

  /// <summary>The end address a BSAVE header declares, or -1 if it is not one.</summary>
  private static int _EndAddress(ReadOnlySpan<byte> data)
    => data[1] != 0 || data[2] != 0 || data[5] != 0 || data[6] != 0 ? -1 : data[3] | (data[4] << 8);

  /// <summary>
  /// Returns the video memory, unpacking it where the file says it is packed.
  /// </summary>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    switch (data[0]) {
      case _STORED:
        if (data.Length < _FULL_SIZE || _EndAddress(data) < _FULL_SIZE - 8)
          throw new InvalidDataException("A Screen 12 picture does not hold a whole screen.");

        return data.ToArray();

      case _PACKED: {
        if (7 + _EndAddress(data) != data.Length)
          throw new InvalidDataException("A packed Screen 12 picture is not the length it declares.");

        // Running out is not an error: what the stream did not reach stays black, which is what
        // the machine's memory would have held.
        var unpacked = new byte[_FULL_SIZE];
        var at = 7;
        for (var i = 7; i < _FULL_SIZE && at < data.Length;) {
          int count, value;
          var command = data[at++];

          switch (command) {
            // A long run: the count follows, and zero means the longest one.
            case 0:
              if (at + 1 >= data.Length)
                return unpacked;

              count = data[at++];
              if (count == 0)
                count = 256;

              value = data[at++];
              break;

            // A short run, the command itself being the count.
            case <= 15:
              if (at >= data.Length)
                return unpacked;

              count = command;
              value = data[at++];
              break;

            // Anything above fifteen is a byte standing for itself, which is why runs of two to
            // fifteen are the only ones worth writing the short way.
            default:
              count = 1;
              value = command;
              break;
          }

          while (count-- > 0 && i < _FULL_SIZE)
            unpacked[i++] = (byte)value;
        }

        return unpacked;
      }

      default:
        throw new InvalidDataException("Not a Screen 12 picture.");
    }
  }

  public static MsxSccFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
