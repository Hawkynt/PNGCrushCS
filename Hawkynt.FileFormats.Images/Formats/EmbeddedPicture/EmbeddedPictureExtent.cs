using System;

namespace FileFormat.EmbeddedPicture;

/// <summary>How far a picture stored inside somebody else's file actually reaches.</summary>
/// <remarks>
/// A container that stores whole picture files end to end states a length for each of them, and a
/// reader that believes the length without checking it will happily hand over a run of bytes that
/// is half one picture and half the next. What settles it is the picture's own framing: a JPEG runs
/// from its start-of-image marker to its end-of-image marker, a PNG from its signature to the end of
/// its <c>IEND</c> chunk, and neither of those can be made to agree with a wrong length by accident.
/// <para/>
/// So this measures a payload from its own bytes and the caller compares that against what the
/// container said. Where the two agree the picture has been found; where they do not, something has
/// been misread and the run is refused rather than drawn.
/// </remarks>
internal static class EmbeddedPictureExtent {

  /// <summary>Measures the picture beginning at a position, or answers -1 if none does.</summary>
  public static int Measure(ReadOnlySpan<byte> data, int at) {
    if (at < 0 || at >= data.Length)
      return -1;

    var rest = data[at..];

    if (rest.Length >= 8 && rest[..8].SequenceEqual([(byte)0x89, (byte)'P', (byte)'N', (byte)'G', (byte)'\r', (byte)'\n', (byte)0x1A, (byte)'\n']))
      return _MeasurePng(rest);

    if (rest.Length >= 4 && rest[0] == 0xFF && rest[1] == 0xD8 && rest[2] == 0xFF)
      return _MeasureJpeg(rest);

    return -1;
  }

  /// <summary>Walks a PNG's chunks to the end of <c>IEND</c>.</summary>
  private static int _MeasurePng(ReadOnlySpan<byte> data) {
    var at = 8;

    while (at + 12 <= data.Length) {
      var length = ((long)data[at] << 24) | ((long)data[at + 1] << 16) | ((long)data[at + 2] << 8) | data[at + 3];
      var isEnd = data[at + 4] == 'I' && data[at + 5] == 'E' && data[at + 6] == 'N' && data[at + 7] == 'D';

      var next = at + 12 + length;
      if (next > data.Length)
        return -1;

      at = (int)next;
      if (isEnd)
        return at;
    }

    return -1;
  }

  /// <summary>Walks a JPEG's markers to its end-of-image, stepping over the entropy data.</summary>
  private static int _MeasureJpeg(ReadOnlySpan<byte> data) {
    var at = 2;

    while (at + 1 < data.Length) {
      if (data[at] != 0xFF)
        return -1;

      var marker = data[at + 1];

      // A run of fill bytes may stand before any marker.
      if (marker == 0xFF) {
        ++at;
        continue;
      }

      // The markers that stand alone, carrying no segment behind them.
      if (marker == 0xD8 || marker == 0x01 || marker is >= 0xD0 and <= 0xD7) {
        at += 2;
        continue;
      }

      if (marker == 0xD9)
        return at + 2;

      if (at + 3 >= data.Length)
        return -1;

      var length = (data[at + 2] << 8) | data[at + 3];
      if (length < 2)
        return -1;

      at += 2 + length;

      if (marker != 0xDA)
        continue;

      // Entropy data has no length. It runs to the next marker that is neither a stuffed zero nor a
      // restart, which is what says where the scan ended.
      while (at + 1 < data.Length) {
        if (data[at] == 0xFF && data[at + 1] != 0x00 && data[at + 1] is not (>= 0xD0 and <= 0xD7))
          break;

        ++at;
      }
    }

    return -1;
  }
}
