using System;
using System.IO;

namespace FileFormat.Crd;

/// <summary>Reads PowerCard maker documents (.crd) from bytes, streams, or file paths.</summary>
public static class CrdReader {

  private const byte _MARKER = 0xFF;
  private const byte _SOI = 0xD8;
  private const byte _APP0 = 0xE0;
  private const byte _EOI = 0xD9;
  private const byte _SOS = 0xDA;

  /// <summary>The APP0 segment has to be long enough to hold its own identifier.</summary>
  private const int _MINIMUM_APP0_LENGTH = 7;

  public static CrdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PowerCard maker document not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CrdFile FromStream(Stream stream) {
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

  public static CrdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CrdFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CrdFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a PowerCard maker document (need at least {CrdFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..CrdFile.Magic.Length].SequenceEqual(CrdFile.Magic))
      throw new InvalidDataException("Not a PowerCard maker document: it does not open with the length-prefixed name this format uses.");

    var start = _FindPicture(data);
    if (start < 0)
      throw new InvalidDataException("A PowerCard maker document was read but there is no JFIF JPEG anywhere inside it.");

    var length = _JpegLength(data, start);
    var picture = data.Slice(start, length).ToArray();

    return new() { PictureOffset = start, PictureData = picture };
  }

  /// <summary>
  /// Finds the picture the way the format's own reader does: by looking for the four letters of the
  /// JFIF identifier and stepping six bytes back to the JPEG they belong to. The four bytes uncovered
  /// by that step have to be a real SOI and APP0 pair, so what is found is a picture and not four
  /// letters that happen to stand in the document text.
  /// </summary>
  private static int _FindPicture(ReadOnlySpan<byte> data) {
    for (var at = CrdFile.HeaderSize; at + 4 <= data.Length; ++at) {
      if (data[at] != 'J' || data[at + 1] != 'F' || data[at + 2] != 'I' || data[at + 3] != 'F')
        continue;

      var start = at - CrdFile.JfifIdentifierOffset;
      if (start < 0)
        continue;

      if (data[start] != _MARKER || data[start + 1] != _SOI || data[start + 2] != _MARKER || data[start + 3] != _APP0)
        continue;

      var segment = (data[start + 4] << 8) | data[start + 5];
      if (segment < _MINIMUM_APP0_LENGTH)
        continue;

      return start;
    }

    return -1;
  }

  /// <summary>
  /// How long the JPEG at <paramref name="start"/> says it is: its own marker chain walked to the
  /// EOI that closes it. The document does not state the length anywhere this reader can see, so the
  /// payload's own framing is what says where it ends, and it has to end inside the file.
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

    throw new InvalidDataException("The JPEG inside a PowerCard maker document is not closed by an end-of-image marker inside the file.");
  }
}
