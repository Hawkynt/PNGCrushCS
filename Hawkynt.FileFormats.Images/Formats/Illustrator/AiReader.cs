using System;
using System.IO;
using System.Text;
using FileFormat.PostScript;

namespace FileFormat.Illustrator;

/// <summary>Opens an Illustrator file and resolves native raster artwork before generic PostScript rendering.</summary>
public static class AiReader {

  private const int _HeaderScan = 4096;

  public static AiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Illustrator file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AiFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static AiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AiFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException($"An Illustrator file of {data.Length} bytes holds no drawing.");

    if (data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F')
      throw new InvalidDataException(
        "This is an Illustrator file of version 9 or later, which is a PDF document under an Illustrator name. " +
        "The PDF reader is the one that opens it."
      );

    var program = PostScriptReader.FromSpan(data);
    if (AiNativeRaster.TryDecode(data, out var raster))
      return new() { Program = program, Raster = raster, Version = _Version(data) };

    return new() { Program = program, Version = _Version(data) };
  }

  private static string? _Version(ReadOnlySpan<byte> data) {
    var text = Encoding.Latin1.GetString(data[..Math.Min(data.Length, _HeaderScan)]);
    foreach (var prefix in (string[])["%%AI8_CreatorVersion:", "%AI5_FileFormat", "%AI3_FileFormat"]) {
      var at = text.IndexOf(prefix, StringComparison.Ordinal);
      if (at < 0)
        continue;

      var end = text.IndexOfAny(['\r', '\n', '%'], at + prefix.Length);
      var value = (end < 0 ? text[(at + prefix.Length)..] : text[(at + prefix.Length)..end]).Trim();
      if (value.Length > 0)
        return $"{prefix.Trim('%', ':')} {value}";
    }

    return null;
  }
}
