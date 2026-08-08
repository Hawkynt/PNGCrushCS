using System;

namespace FileFormat.AtariPlayerEditor;

/// <summary>Assembles an Atari Player Editor sheet from an <see cref="AtariPlayerEditorFile"/>.</summary>
public static class AtariPlayerEditorWriter {

  /// <summary>
  /// Writes the sheet, which is a fixed 1677 bytes whether it holds one frame or sixteen because the
  /// editor wrote its whole workspace out.
  /// </summary>
  public static byte[] ToBytes(AtariPlayerEditorFile file) {
    var data = new byte[AtariPlayerEditorFile.FileSize];
    var source = file.Data ?? [];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);

    return data;
  }
}
