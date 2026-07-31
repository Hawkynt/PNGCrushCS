using System;
using System.IO;

namespace FileFormat.ApplePreferred;

/// <summary>Reads Apple Preferred Format pictures from bytes, streams, or file paths.</summary>
public static class ApplePreferredReader {

  /// <summary>Rows a MULTIPAL chunk carries palettes for.</summary>
  private const int _MULTIPAL_HEIGHT = 200;

  public static ApplePreferredFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ApplePreferredFile FromStream(Stream stream) {
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

  public static ApplePreferredFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 1249 || data[4] != 4 || !_IsStringAt(data, 5, "MAIN") || data[14] != 0)
      throw new InvalidDataException("Not an Apple Preferred Format picture.");

    int paletteCount = data[13];
    if (paletteCount > 16)
      throw new InvalidDataException($"A picture cannot carry {paletteCount} palettes.");

    var directoryOffset = ApplePreferredFile.PalettesOffset + 2 + paletteCount * Core.AppleIIGSGraphics.PaletteSize;
    if (directoryOffset >= data.Length)
      throw new InvalidDataException("A picture's palettes run past the end of the file.");

    // The mode lives in the high nibble of a byte whose low nibble means nothing here, and each
    // scanline repeats it — a line claiming the other mode is a corrupt file, not a mixed picture.
    var mode = data[9] & 240;
    var width = data[11] | (data[12] << 8);
    var storedHeight = data[directoryOffset - 2] | (data[directoryOffset - 1] << 8);

    var wide = mode switch {
      0 => false,
      128 => true,
      _ => throw new InvalidDataException($"Screen mode {mode} is neither of the two that exist."),
    };

    if (wide ? (width & 3) != 0 : (width & 1) != 0)
      throw new InvalidDataException($"A picture {width} pixels across does not fill whole bytes.");

    if (width <= 0 || storedHeight <= 0)
      throw new InvalidDataException($"A picture of {width}x{storedHeight} is empty.");

    var bitmapOffset = directoryOffset + storedHeight * ApplePreferredFile.DirectoryEntrySize;
    if (bitmapOffset >= data.Length)
      throw new InvalidDataException("A picture's directory runs past the end of the file.");

    // Only pictures the height of a screen can carry per-line palettes, so shorter ones are not
    // even searched — which also spares the chunk walk on the many files that have no second chunk.
    var multipalOffset = storedHeight == _MULTIPAL_HEIGHT ? _FindMultipal(data) : -1;

    if (multipalOffset < 0) {
      for (var y = 0; y < storedHeight; ++y) {
        var entry = directoryOffset + y * ApplePreferredFile.DirectoryEntrySize;
        var lineMode = data[entry + 2];
        if ((lineMode & 240) != mode || (lineMode & 15) >= paletteCount || data[entry + 3] != 0)
          throw new InvalidDataException($"Scanline {y} names a palette or a mode the picture has not got.");
      }
    }

    return new() {
      Data = data.ToArray(),
      Width = width,
      Height = wide ? storedHeight << 1 : storedHeight,
      StoredHeight = storedHeight,
      IsWideMode = wide,
      DirectoryOffset = directoryOffset,
      BitmapOffset = bitmapOffset,
      MultipalOffset = multipalOffset,
    };
  }

  /// <summary>Walks the chunk chain looking for a MULTIPAL, and reports where its palettes start.</summary>
  private static int _FindMultipal(ReadOnlySpan<byte> data) {
    var at = 0;

    for (var length = _Length(data, 0); ; length = _Length(data, at)) {
      if (length <= 0)
        throw new InvalidDataException("A chunk of no length would never end the chain.");

      at += length;
      if (at < 0 || at > data.Length - ApplePreferredFile.MultipalChunkSize)
        return -1;

      if (_Length(data, at) == ApplePreferredFile.MultipalChunkSize && data[at + 4] == 8
          && _IsStringAt(data, at + 5, "MULTIPAL") && data[at + 13] == _MULTIPAL_HEIGHT && data[at + 14] == 0)
        return at + 15;
    }
  }

  private static int _Length(ReadOnlySpan<byte> data, int offset)
    => offset + 3 < data.Length
      ? data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24)
      : 0;

  private static bool _IsStringAt(ReadOnlySpan<byte> data, int offset, string text) {
    if (offset + text.Length > data.Length)
      return false;

    for (var i = 0; i < text.Length; ++i) {
      if (data[offset + i] != text[i])
        return false;
    }

    return true;
  }

  public static ApplePreferredFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
