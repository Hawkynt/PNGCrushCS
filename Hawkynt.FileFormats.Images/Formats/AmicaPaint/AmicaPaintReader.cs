using System;
using System.IO;

namespace FileFormat.AmicaPaint;

/// <summary>Reads Commodore 64 Amica Paint (.ami) files from bytes, streams, or file paths.</summary>
public static class AmicaPaintReader {

  public static AmicaPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Amica Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AmicaPaintFile FromStream(Stream stream) {
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

  public static AmicaPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AmicaPaintFile.LoadAddressSize + 1)
      throw new InvalidDataException($"Data too small for a valid Amica Paint file (got {data.Length} bytes).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    // A file shorter than a whole screen is packed, which every one in the corpus is.
    var payload = data.Length == AmicaPaintFile.ExpectedFileSize
      ? data[AmicaPaintFile.LoadAddressSize..].ToArray()
      : _Unpack(data);

    if (payload.Length < AmicaPaintFile.UnpackedSize)
      throw new InvalidDataException($"An Amica Paint screen is {AmicaPaintFile.UnpackedSize} bytes; this one came to {payload.Length}.");

    ReadOnlySpan<byte> body = payload;
    var offset = 0;

    var bitmapData = new byte[AmicaPaintFile.BitmapDataSize];
    body.Slice(offset, AmicaPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += AmicaPaintFile.BitmapDataSize;

    var screenRam = new byte[AmicaPaintFile.ScreenRamSize];
    body.Slice(offset, AmicaPaintFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += AmicaPaintFile.ScreenRamSize;

    var colorRam = new byte[AmicaPaintFile.ColorRamSize];
    body.Slice(offset, AmicaPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += AmicaPaintFile.ColorRamSize;

    var backgroundColor = body[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
    }

  /// <summary>
  /// Expands the packed screen: 0xC2 introduces a count and then the byte to repeat.
  /// </summary>
  /// <remarks>
  /// Established against RECOIL, which draws all three samples: read this way each expands to exactly
  /// the 10001 bytes a screen takes and every pixel falls in the same region as RECOIL's. Reading the
  /// pair the other way round — value first, then count — none of them reaches a screen's worth and
  /// barely half the pixels land right.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var screen = new byte[AmicaPaintFile.UnpackedSize];
    var written = 0;
    var pos = AmicaPaintFile.LoadAddressSize;

    while (pos < data.Length && written < screen.Length) {
      var value = data[pos];
      if (value != AmicaPaintFile.RunEscape || pos + 2 >= data.Length) {
        screen[written++] = value;
        ++pos;
        continue;
      }

      var run = Math.Min(data[pos + 1], screen.Length - written);
      screen.AsSpan(written, run).Fill(data[pos + 2]);
      written += run;
      pos += 3;
    }

    return written < screen.Length ? screen[..written] : screen;
  }

  public static AmicaPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
