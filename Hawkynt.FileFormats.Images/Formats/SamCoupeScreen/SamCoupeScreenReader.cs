using System;
using System.IO;

namespace FileFormat.SamCoupeScreen;

/// <summary>Reads SAM Coupe mode 1, 2 and 3 screens from bytes, streams, or file paths.</summary>
public static class SamCoupeScreenReader {

  public static SamCoupeScreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SAM Coupe screen not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName), SamCoupeScreenFile.ModeFromExtension(file.Extension));
  }

  public static SamCoupeScreenFile FromStream(Stream stream) {
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

  /// <summary>Reads a screen, working the mode out from the length.</summary>
  /// <remarks>
  /// The extension is what really names the mode, and <see cref="FromFile"/> uses it. Given bytes
  /// alone the only evidence is where the interrupt list ends, and that is not conclusive: all
  /// three offsets are four-byte aligned, so a mode 2 screen's terminator sits exactly where a walk
  /// started at mode 1's offset would also expect one. The largest mode the file can hold wins,
  /// because the smaller reading would have to explain thousands of interrupt records — 1856 of
  /// them to stretch a mode 1 file to mode 2's length — where a real screen has at most a handful
  /// per scanline.
  /// </remarks>
  public static SamCoupeScreenFile FromSpan(ReadOnlySpan<byte> data) {
    foreach (var candidate in new[] { SamCoupeScreenMode.Mode3, SamCoupeScreenMode.Mode2, SamCoupeScreenMode.Mode1 })
      if (_IsWellFormed(data, candidate))
        return new() { Mode = candidate, Data = data.ToArray() };

    throw new InvalidDataException(
      $"Not a SAM Coupe mode 1, 2 or 3 screen: {data.Length} bytes with no interrupt list ending where any of them expects.");
  }

  public static SamCoupeScreenFile FromSpan(ReadOnlySpan<byte> data, SamCoupeScreenMode mode) {
    if (!_IsWellFormed(data, mode))
      throw new InvalidDataException(
        $"Not a SAM Coupe mode {(int)mode} screen: {data.Length} bytes with no terminated interrupt list at {SamCoupeScreenFile.InterruptOffsetFor(mode)}.");

    return new() { Mode = mode, Data = data.ToArray() };
  }

  /// <summary>Whether the interrupt list starts where the mode says and ends with the terminator.</summary>
  private static bool _IsWellFormed(ReadOnlySpan<byte> data, SamCoupeScreenMode mode) {
    var offset = SamCoupeScreenFile.InterruptOffsetFor(mode);
    if (data.Length <= offset)
      return false;

    // Records are four bytes each until the terminator; anything that runs off the end is not one.
    while (data[offset] != SamCoupeScreenFile.InterruptTerminator) {
      offset += SamCoupeScreenFile.InterruptRecordSize;
      if (offset >= data.Length)
        return false;
    }

    return offset + 1 == data.Length;
  }

  public static SamCoupeScreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
