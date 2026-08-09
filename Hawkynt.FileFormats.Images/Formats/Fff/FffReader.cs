using System;
using System.IO;

namespace FileFormat.Fff;

/// <summary>Reads MAGGI Hairstyles &amp; Cosmetics files (.fff) from bytes, streams, or file paths.</summary>
public static class FffReader {

  private const byte _MARKER = 0xFF;
  private const byte _SOI = 0xD8;
  private const byte _EOI = 0xD9;
  private const byte _SOS = 0xDA;

  public static FffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MAGGI Hairstyles & Cosmetics file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FffFile FromStream(Stream stream) {
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

  public static FffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static FffFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FffFile.SignatureOffset + FffFile.SignatureSize)
      throw new InvalidDataException($"Data too small for a MAGGI Hairstyles & Cosmetics file (the signature alone runs to byte {FffFile.SignatureOffset + FffFile.SignatureSize} and there are {data.Length}).");

    if (!data.Slice(FffFile.SignatureOffset, FffFile.SignatureSize).SequenceEqual(FffFile.Magic))
      throw new InvalidDataException($"Not a MAGGI Hairstyles & Cosmetics file: byte {FffFile.SignatureOffset} does not carry \"{FffFile.SignatureText}\".");

    if (data.Length <= FffFile.PictureOffset + 1)
      throw new InvalidDataException($"The portrait stands at byte {FffFile.PictureOffset} and the file has {data.Length}.");

    if (data[FffFile.PictureOffset] != _MARKER || data[FffFile.PictureOffset + 1] != _SOI)
      throw new InvalidDataException($"Byte {FffFile.PictureOffset} of a MAGGI Hairstyles & Cosmetics file is where the portrait stands and there is no JPEG there.");

    var length = _JpegLength(data, FffFile.PictureOffset);
    return new() { PictureData = data.Slice(FffFile.PictureOffset, length).ToArray() };
  }

  /// <summary>
  /// How long the JPEG at <paramref name="start"/> says it is: its own marker chain walked to the
  /// EOI that closes it. The record states no length, so the payload's own framing is what says
  /// where it ends, and it has to end inside the file.
  /// </summary>
  private static int _JpegLength(ReadOnlySpan<byte> data, int start) {
    var at = start + 2;
    while (at + 1 < data.Length) {
      if (data[at] != _MARKER) {
        ++at;
        continue;
      }

      var marker = data[at + 1];
      if (marker is _MARKER or 0x00) {
        ++at;
        continue;
      }

      if (marker == _EOI)
        return at + 2 - start;

      if (marker is _SOI or >= 0xD0 and <= 0xD7 or 0x01) {
        at += 2;
        continue;
      }

      if (at + 3 >= data.Length)
        break;

      var length = (data[at + 2] << 8) | data[at + 3];
      if (length < 2)
        break;

      at += 2 + length;

      // Behind a start-of-scan the bytes are entropy coded, so walk them to the next real marker
      // rather than trying to read a length that is not there.
      if (marker != _SOS)
        continue;

      while (at + 1 < data.Length) {
        if (data[at] != _MARKER) {
          ++at;
          continue;
        }

        var next = data[at + 1];
        if (next is 0x00 or _MARKER or >= 0xD0 and <= 0xD7) {
          at += next == _MARKER ? 1 : 2;
          continue;
        }

        break;
      }
    }

    throw new InvalidDataException("The portrait inside a MAGGI Hairstyles & Cosmetics file is not closed by an end-of-image marker inside the file.");
  }
}
