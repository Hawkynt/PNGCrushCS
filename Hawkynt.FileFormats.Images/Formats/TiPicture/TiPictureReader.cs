using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.TiPicture;

/// <summary>Reads TI-82/83/85/86 picture variables from bytes, streams, or file paths.</summary>
public static class TiPictureReader {

  public static TiPictureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TI picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TiPictureFile FromStream(Stream stream) {
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

  public static TiPictureFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < TiPictureFile.HeaderSize + 2)
      throw new InvalidDataException("Data too small for a valid TI picture file.");

    var width = _ScreenWidth(data);

    // The two bytes after the comment give the length of everything up to the checksum, so the
    // header, that length and the checksum are the whole file. A file where they are not is one
    // whose signature matched by accident.
    var stated = BinaryPrimitives.ReadUInt16LittleEndian(data[(TiPictureFile.HeaderSize - 2)..]);
    if (TiPictureFile.HeaderSize + stated + 2 != data.Length)
      throw new InvalidDataException(
        $"A TI picture states {stated} bytes of entries, which with its header and checksum makes "
        + $"{TiPictureFile.HeaderSize + stated + 2} and the file is {data.Length}.");

    var rowBytes = (width + 7) / 8;
    var expected = rowBytes * TiPictureFile.ScreenHeight;

    // Entries run one after another and a picture need not be the first, so they are walked rather
    // than assumed. Each states the length of its own header, which is what lets one walk cope with
    // the TI-85's counted name and the TI-86's padded one without knowing which it is looking at.
    var at = TiPictureFile.HeaderSize;
    var end = TiPictureFile.HeaderSize + stated;
    while (at + 4 <= end) {
      var entryHeader = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
      if (entryHeader < 3 || at + 4 + entryHeader > end)
        throw new InvalidDataException($"A TI picture entry at {at} states a {entryHeader} byte header.");

      var length = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2)..]);
      var type = data[at + 4];
      var body = at + 4 + entryHeader;
      if (body + length > end)
        throw new InvalidDataException($"A TI picture entry at {at} states {length} bytes and the file has fewer.");

      // The length is given once inside the entry header and again just before the data; disagreeing
      // copies mean this is not being read as an entry at all.
      var repeat = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2 + entryHeader)..]);
      if (repeat != length)
        throw new InvalidDataException($"A TI picture entry at {at} gives its length as {length} and then as {repeat}.");

      if (type is TiPictureFile.PictureType8283 or TiPictureFile.PictureType8586 && length >= 2) {
        // The picture states its own size a third time, and it must be the screen of the calculator
        // named in the signature. That is what stops a variable of some other kind whose type byte
        // collides from being drawn as a picture.
        var inner = BinaryPrimitives.ReadUInt16LittleEndian(data[body..]);
        if (inner == length - 2 && inner == expected) {
          var pixels = new byte[expected];
          data.Slice(body + 2, expected).CopyTo(pixels);

          return new() {
            Width = width,
            Height = TiPictureFile.ScreenHeight,
            Model = $"{(char)data[4]}{(char)data[5]}",
            PixelData = pixels,
          };
        }
      }

      at = body + length;
    }

    throw new InvalidDataException("A TI transfer file carries a picture in an entry and this one has none.");
  }

  public static TiPictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>The screen the signature says this file came off.</summary>
  private static int _ScreenWidth(ReadOnlySpan<byte> data) {
    if (data[0] != '*' || data[1] != '*' || data[6] != '*' || data[7] != '*'
        || data[2] != 'T' || data[3] != 'I')
      throw new InvalidDataException("Not a TI transfer file: it does not open with a **TInn** signature.");

    return (char)data[4] switch {
      // The TI-73 has the TI-82's screen and the TI-82's container; the signature is the only thing
      // that differs, which is why it belongs on this arm rather than an arm of its own.
      '7' when data[5] is (byte)'3' => TiPictureFile.Width8283,
      '8' when data[5] is (byte)'2' or (byte)'3' => TiPictureFile.Width8283,
      '8' when data[5] is (byte)'5' or (byte)'6' => TiPictureFile.Width8586,
      _ => throw new InvalidDataException($"A TI transfer file for the {(char)data[4]}{(char)data[5]} is not one this reads."),
    };
  }
}
