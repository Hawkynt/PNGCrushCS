using System;
using System.IO;

namespace FileFormat.TechnicolorDream;

/// <summary>Reads Technicolor Dream pictures from bytes, streams, or file paths.</summary>
public static class TechnicolorDreamReader {

  /// <summary>
  /// Reads a picture, taking the hues from the .col file beside it when there is one.
  /// </summary>
  public static TechnicolorDreamFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    var picture = FromBytes(File.ReadAllBytes(file.FullName));
    var hues = _TryReadCompanion(file);

    return hues == null ? picture : picture with { Hues = hues };
  }

  /// <summary>Looks for the hue field beside the luminance one, trying both letter cases.</summary>
  private static byte[]? _TryReadCompanion(FileInfo file) {
    var directory = file.DirectoryName;
    if (directory == null)
      return null;

    var stem = Path.GetFileNameWithoutExtension(file.Name);
    foreach (var extension in (string[])[".col", ".COL"]) {
      var candidate = new FileInfo(Path.Combine(directory, stem + extension));
      if (!candidate.Exists || candidate.Length != TechnicolorDreamFile.FileSize)
        continue;

      return File.ReadAllBytes(candidate.FullName);
    }

    return null;
  }

  public static TechnicolorDreamFile FromStream(Stream stream) {
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

  public static TechnicolorDreamFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != TechnicolorDreamFile.FileSize)
      throw new InvalidDataException(
        $"A Technicolor Dream field is {TechnicolorDreamFile.FileSize} bytes, got {data.Length}.");

    return new() { Luminances = data.ToArray() };
  }

  public static TechnicolorDreamFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
