using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.SuperHiresFli;

/// <summary>Assembles Super Hires FLI Editor (.shf) file bytes.</summary>
/// <remarks>
/// Written packed, which is the narrow form. The wide form is a fixed length and would need no
/// packing, but its sprite pointers address bytes the bitmap also uses, so a freely drawn picture
/// cannot be written into it — see <see cref="SuperHiresFliFile.FromRawImage"/>.
/// </remarks>
public static class SuperHiresFliWriter {

  /// <summary>The longest run one count can express, a count of zero standing for 256.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>The shortest run worth writing, a run costing three bytes.</summary>
  private const int _MIN_RUN = 4;

  public static byte[] ToBytes(SuperHiresFliFile file) {
    var data = file.Data ?? [];
    if (file.HasSprites || data.Length != SuperHiresFliFile.UnpackedSize)
      throw new InvalidDataException(
        $"A Super Hires FLI picture is written from {SuperHiresFliFile.UnpackedSize} unpacked bytes, got {data.Length}.");

    var escape = _LeastUsedByte(data);
    var packed = new List<byte> {
      // Where the editor's own loader put the picture.
      0x00, 0x40,
      escape,
    };

    for (var at = 0; at < data.Length;) {
      var value = data[at];
      var run = 1;
      while (run < _MAX_RUN && at + run < data.Length && data[at + run] == value)
        ++run;

      // A byte that is the escape has to be introduced even when it stands alone, since writing it
      // plainly would start a run that is not there.
      if (run >= _MIN_RUN || value == escape) {
        packed.Add(escape);
        packed.Add((byte)(run == _MAX_RUN ? 0 : run));
        packed.Add(value);
      } else
        for (var i = 0; i < run; ++i)
          packed.Add(value);

      at += run;
    }

    // The wide form is recognised by its length alone, so a packed file must not happen to have it.
    if (packed.Count == SuperHiresFliFile.WideFileSize)
      packed.Add(0);

    return [.. packed];
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
