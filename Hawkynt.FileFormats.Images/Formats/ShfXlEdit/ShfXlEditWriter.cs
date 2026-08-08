using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.ShfXlEdit;

/// <summary>Assembles SHF-XL Edit (.shx) file bytes.</summary>
/// <remarks>
/// The run-length encoding runs backwards from the end of the file, so the packer works over the
/// picture reversed and then reverses what it produced. The escape byte ends up last because it is
/// the first thing a backwards reader meets, and within a run the count precedes the value for the
/// same reason.
/// </remarks>
public static class ShfXlEditWriter {

  /// <summary>The longest run one count can express, a count of zero standing for 256.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>The shortest run worth writing, a run costing three bytes.</summary>
  private const int _MIN_RUN = 4;

  public static byte[] ToBytes(ShfXlEditFile file) {
    var data = file.Data ?? [];
    if (file.IsRaw || data.Length != ShfXlEditFile.UnpackedSize)
      throw new InvalidDataException(
        $"An SHF-XL picture is written from {ShfXlEditFile.UnpackedSize} unpacked bytes, got {data.Length}.");

    var escape = _LeastUsedByte(data);

    // In the order the backwards reader meets them: the escape first, then the picture from its
    // last byte towards its first.
    var stream = new List<byte> { escape };

    for (var at = data.Length; at > 0;) {
      var value = data[at - 1];
      var run = 1;
      while (run < _MAX_RUN && at - run - 1 >= 0 && data[at - run - 1] == value)
        ++run;

      // A byte that is the escape has to be introduced even alone, since written plainly it would
      // start a run that is not there.
      if (run >= _MIN_RUN || value == escape) {
        stream.Add(escape);
        stream.Add((byte)(run == _MAX_RUN ? 0 : run));
        stream.Add(value);
      } else
        for (var i = 0; i < run; ++i)
          stream.Add(value);

      at -= run;
    }

    // The form that is a copy of video memory is recognised by its length alone, so a packed file
    // must not happen to have it.
    if (stream.Count + 2 == ShfXlEditFile.RawFileSize)
      stream.Add(0);

    var packed = new byte[stream.Count + 2];

    // Where the editor's own loader put the picture.
    packed[1] = 0x40;
    for (var i = 0; i < stream.Count; ++i)
      packed[^(i + 1)] = stream[i];

    return packed;
  }

  /// <summary>The byte value the picture uses least, which costs least to spend as the escape.</summary>
  private static byte _LeastUsedByte(ReadOnlySpan<byte> data) {
    Span<int> counts = stackalloc int[256];
    foreach (var value in data)
      ++counts[value];

    var best = (byte)0;
    for (var candidate = 1; candidate < counts.Length; ++candidate)
      if (counts[candidate] < counts[best])
        best = (byte)candidate;

    return best;
  }
}
