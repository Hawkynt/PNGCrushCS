using System;
using System.IO;

namespace FileFormat.PrintfoxPagefox;

/// <summary>Reads Printfox/Pagefox (.bs/.pg) files from bytes, streams, or file paths.</summary>
public static class PrintfoxPagefoxReader {

  public static PrintfoxPagefoxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Printfox/Pagefox file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrintfoxPagefoxFile FromStream(Stream stream) {
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

  /// <summary>
  /// Expands the packed screen.
  /// </summary>
  /// <remarks>
  /// Every real file of this format is packed, and this used to hand the packed bytes back as though
  /// they were the picture. The one it accepted was drawn from its own compressed data; the other
  /// three were refused for being smaller than a screen, which every one of them is.
  /// <para/>
  /// The packing: a type byte, then bytes that stand for themselves except 0x9B, which introduces a
  /// count as a little-endian word and then the byte to repeat. Worked out by rebuilding the screens
  /// RECOIL draws and reading the files against them — 0x9B 0xC5 0x02 0x00 is the 709 zero bytes one
  /// of them opens with, and 0x9B 0x72 0x00 0x00 the 114 that follow its first three set bytes.
  /// <para/>
  /// The screen is held a character cell at a time, eight bytes to a cell, the way the machine's own
  /// bitmap is. All three samples now expand to exactly the 8000 bytes a screen takes and match
  /// RECOIL byte for byte; a few bytes past the screen are left alone, being no part of it.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var screen = new byte[PrintfoxPagefoxFile.MinDataSize];
    var written = 0;
    var pos = 1;

    while (pos < data.Length && written < screen.Length) {
      var control = data[pos];
      if (control != _RUN_ESCAPE || pos + 3 >= data.Length) {
        screen[written++] = control;
        ++pos;
        continue;
      }

      var run = Math.Min(data[pos + 1] | (data[pos + 2] << 8), screen.Length - written);
      screen.AsSpan(written, run).Fill(data[pos + 3]);
      written += run;
      pos += 4;
    }

    if (written < screen.Length)
      throw new InvalidDataException($"A Printfox picture is {screen.Length} bytes of screen; this one ran out after {written}.");

    return screen;
  }

  /// <summary>Puts a screen held a character cell at a time back into rows.</summary>
  private static byte[] _CellsToRows(byte[] cells) {
    var rows = new byte[cells.Length];
    var columns = PrintfoxPagefoxFile.BytesPerRow;

    for (var cellRow = 0; cellRow < PrintfoxPagefoxFile.FixedHeight / 8; ++cellRow)
      for (var cellColumn = 0; cellColumn < columns; ++cellColumn)
        for (var line = 0; line < 8; ++line)
          rows[(cellRow * 8 + line) * columns + cellColumn] = cells[(cellRow * columns + cellColumn) * 8 + line];

    return rows;
  }

  private const byte _RUN_ESCAPE = 0x9B;

  public static PrintfoxPagefoxFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < 8)
      throw new InvalidDataException($"Data too small for a valid Printfox/Pagefox file (got {data.Length} bytes).");

    // Pagefox pages carry this and are a different thing entirely: 640 by 584 rather than a screen,
    // and packed some other way — their bytes run strictly high, low, high, low from the first, where
    // this packing would have to open with a run. RECOIL and XnView both draw them. Saying which form
    // it is beats drawing a screen out of a page.
    if (data.Length > 4 && data[0] == 'P' && data[1] == 'I' && data[2] == 'P' && data[3] == 'K')
      throw new InvalidDataException("This is a Pagefox page rather than a Printfox screen, which is not decoded here.");

    return new() {
      RawData = _CellsToRows(_Unpack(data)),
    };
  }

  public static PrintfoxPagefoxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
