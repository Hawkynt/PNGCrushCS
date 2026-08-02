using System;
using System.IO;

namespace FileFormat.BbcMicroScreen;

/// <summary>Reads BBC Micro screen dumps from bytes, streams, or file paths.</summary>
public static class BbcMicroScreenReader {

  public static BbcMicroScreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BBC Micro screen not found.", file.FullName);

    // Only the extension distinguishes a 20480-byte mode 0 dump from mode 1 or 2.
    return FromBytes(File.ReadAllBytes(file.FullName), ModeFromExtension(file.Extension));
  }

  public static BbcMicroScreenFile FromStream(Stream stream) {
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

  /// <summary>Reads a dump, inferring the mode from its length.</summary>
  /// <remarks>10240 bytes is unambiguously mode 4 or 5 and 20480 bytes mode 0, 1 or 2; without an
  /// extension to go on we take the monochrome reading, which is the one that cannot misrepresent
  /// the data as colour that is not there.</remarks>
  public static BbcMicroScreenFile FromSpan(ReadOnlySpan<byte> data) {
    var mode = data.Length switch {
      10240 => BbcMicroMode.Mode4,
      20480 => BbcMicroMode.Mode0,
      _ => throw new InvalidDataException($"A BBC Micro screen is 10240 or 20480 bytes, got {data.Length}.")
    };

    return FromSpan(data, mode);
  }

  public static BbcMicroScreenFile FromSpan(ReadOnlySpan<byte> data, BbcMicroMode mode) {
    var expected = BbcMicroScreenFile.FileSizeFor(mode);
    if (data.Length != expected)
      throw new InvalidDataException($"A {mode} screen is {expected} bytes, got {data.Length}.");

    return new() { Mode = mode, ScreenData = data.ToArray() };
  }

  public static BbcMicroScreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static BbcMicroScreenFile FromBytes(byte[] data, BbcMicroMode mode) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, mode);
  }

  /// <summary>The mode an extension names; the writer needs the same answer the reader gives.</summary>
  internal static BbcMicroMode ModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".bb0" => BbcMicroMode.Mode0,
    ".bb1" => BbcMicroMode.Mode1,
    ".bb2" => BbcMicroMode.Mode2,
    ".bb5" => BbcMicroMode.Mode5,
    _ => BbcMicroMode.Mode4,
  };
}
