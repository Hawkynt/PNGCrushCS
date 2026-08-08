using System;

namespace FileFormat.AtariChampionsInterlace;

/// <summary>Assembles a Champions' Interlace picture from an <see cref="AtariChampionsInterlaceFile"/>.</summary>
public static class AtariChampionsInterlaceWriter {

  /// <summary>
  /// Writes the uncompressed form, whose length is the only thing that says which of the three it is.
  /// </summary>
  /// <remarks>
  /// The compressed form exists and is not written. It packs each field down its own columns rather
  /// than along its rows, which is a saving on disk and nothing on screen — and a file that read
  /// back as a different length would read back as a different picture, since the length is what
  /// says how many registers the picture carries.
  /// </remarks>
  public static byte[] ToBytes(AtariChampionsInterlaceFile file) {
    // A picture that arrived without registers of its own keeps its length: padding it out to the
    // longer form would give it a set of registers that are all black, where its length made the
    // reader fall back to the ramp it was drawn against.
    var source = file.Data ?? [];
    var length = source.Length switch {
      AtariChampionsInterlaceFile.BareSize => AtariChampionsInterlaceFile.BareSize,
      AtariChampionsInterlaceFile.OneSetSize => AtariChampionsInterlaceFile.OneSetSize,
      _ => AtariChampionsInterlaceFile.PerRowSize,
    };

    var data = new byte[length];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);

    return data;
  }
}
