using System;
using System.IO;

namespace FileFormat.CrackArt;

/// <summary>
/// The run-length coding CrackArt uses for a packed picture.
/// </summary>
/// <remarks>
/// What was here before was PackBits, which this is not. A packed CrackArt picture names its own
/// escape byte and then runs plainly: any byte that is not the escape stands for itself, and the
/// escape introduces a count and a value. The count is stored one less than it means, so a run of
/// seventeen is written as sixteen — reading it as written leaves every run a byte short and the
/// picture drifts sideways from there.
/// <para/>
/// The stream simply stops when there is nothing more to say, and whatever is left of the screen
/// stays blank; the sample fills 7578 of its 32000 bytes and the rest is empty.
/// <para/>
/// Settled against RECOIL on a real file: the picture comes back identical.
/// </remarks>
internal static class CrackArtCompressor {

  /// <summary>The escape byte, then three bytes whose meaning no reader here depends on.</summary>
  internal const int PreambleSize = 4;

  /// <summary>The most one run can say, the count being stored one less than it means.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>Packs a screen, choosing an escape byte that the screen itself uses least.</summary>
  public static byte[] Compress(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    // Whichever byte occurs least often costs least to escape when it appears for itself.
    Span<int> frequency = stackalloc int[256];
    foreach (var value in data)
      ++frequency[value];

    var escape = 0;
    for (var i = 1; i < 256; ++i)
      if (frequency[i] < frequency[escape])
        escape = i;

    using var ms = new MemoryStream();
    ms.WriteByte((byte)escape);
    ms.Write([0, 0, 1]);

    var at = 0;
    while (at < data.Length) {
      var value = data[at];
      var run = 1;
      while (at + run < data.Length && data[at + run] == value && run < _MAX_RUN)
        ++run;

      // A run of three pays for itself, and the escape must always be introduced even when alone.
      if (run >= 3 || value == escape) {
        ms.WriteByte((byte)escape);
        ms.WriteByte((byte)(run - 1));
        ms.WriteByte(value);
      } else
        for (var i = 0; i < run; ++i)
          ms.WriteByte(value);

      at += run;
    }

    return ms.ToArray();
  }

  /// <summary>Unpacks a screen, leaving blank whatever the stream does not reach.</summary>
  public static byte[] Decompress(byte[] data, int expectedSize) {
    ArgumentNullException.ThrowIfNull(data);

    var output = new byte[expectedSize];
    if (data.Length < PreambleSize)
      return output;

    var escape = data[0];
    var outIdx = 0;
    var inIdx = PreambleSize;

    while (inIdx < data.Length && outIdx < expectedSize) {
      var value = data[inIdx++];
      if (value != escape) {
        output[outIdx++] = value;
        continue;
      }

      if (inIdx + 1 >= data.Length)
        break;

      var count = data[inIdx++] + 1;
      var repeated = data[inIdx++];
      for (var i = 0; i < count && outIdx < expectedSize; ++i)
        output[outIdx++] = repeated;
    }

    return output;
  }
}
