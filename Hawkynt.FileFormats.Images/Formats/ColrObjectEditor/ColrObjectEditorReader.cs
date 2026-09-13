using System;
using System.IO;

namespace FileFormat.ColrObjectEditor;

/// <summary>Reads C.O.L.R. Object Editor pictures from bytes, streams, or file paths.</summary>
public static class ColrObjectEditorReader {

  /// <summary>Reads a picture, taking its colours from the .pal file beside it when there is one.</summary>
  public static ColrObjectEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Object not found.", file.FullName);

    var picture = FromBytes(File.ReadAllBytes(file.FullName));
    var palette = _TryReadCompanion(file);

    return palette == null ? picture : picture with { Palette = palette };
  }

  private static byte[]? _TryReadCompanion(FileInfo file) {
    var directory = file.DirectoryName;
    if (directory == null)
      return null;

    var stem = Path.GetFileNameWithoutExtension(file.Name);
    foreach (var extension in (string[])[".pal", ".PAL"]) {
      var candidate = new FileInfo(Path.Combine(directory, stem + extension));
      if (!candidate.Exists || candidate.Length != ColrObjectEditorFile.PaletteFileSize)
        continue;

      return File.ReadAllBytes(candidate.FullName);
    }

    return null;
  }

  public static ColrObjectEditorFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static ColrObjectEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != ColrObjectEditorFile.FileSize)
      throw new InvalidDataException($"An object is {ColrObjectEditorFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static ColrObjectEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
