using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Autologic;

/// <summary>Writes Autologic bitmaps (.gm, .gm2, .gm4).</summary>
public static class AutologicWriter {

  /// <summary>The longest run one count byte can state.</summary>
  private const int _MaximumRun = 128;

  public static byte[] ToBytes(AutologicFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture to write.");

    var width = file.Width;
    var height = file.Height;
    if (width is < 1 or > 0xFFFF || height is < 1 or > 0xFFFF)
      throw new InvalidOperationException($"An Autologic bitmap states its size in words, so {width}x{height} cannot be written.");
    if (file.PixelData.Length < width * height)
      throw new InvalidOperationException($"A {width}x{height} Autologic bitmap needs {width * height} samples and only {file.PixelData.Length} were given.");

    var levels = file.Levels is >= 0 and <= 0xFF ? file.Levels : AutologicFile.RawLevels;
    var payload = levels == AutologicFile.RawLevels
      ? _Plain(file.PixelData, width * height)
      : _Coded(file.PixelData, width, height);

    // One record holds the whole payload, so no sample and count pair is ever split across a
    // record boundary — a split pair decodes to one pixel more than it was written for.
    var body = new byte[AutologicFile.HeaderSize + 4 + payload.Length];
    AutologicFile.Magic.CopyTo(body);
    _WriteWord(body, 4, width);
    _WriteWord(body, 6, height);
    body[17] = (byte)levels;
    _WriteWord(body, AutologicFile.HeaderSize, AutologicFile.DataRecordTag);
    _WriteWord(body, AutologicFile.HeaderSize + 2, payload.Length / 2);
    payload.CopyTo(body.AsSpan(AutologicFile.HeaderSize + 4));
    return body;
  }

  private static byte[] _Plain(byte[] samples, int count) {
    var payload = new byte[count + (count & 1)];
    samples.AsSpan(0, count).CopyTo(payload);
    return payload;
  }

  private static byte[] _Coded(byte[] samples, int width, int height) {
    var payload = new List<byte>(width * height / 2 + 8);
    for (var y = 0; y < height; ++y) {
      var x = 0;
      while (x < width) {
        var value = samples[y * width + x];
        if (value > 0x7F)
          throw new InvalidOperationException($"The Autologic line-art coding carries seven bit samples and {value} does not fit; write the 255 form for eight bit greys.");

        var run = 1;
        while (x + run < width && samples[y * width + x + run] == value && run < _MaximumRun)
          ++run;

        payload.Add(value);
        if (run > 1)
          payload.Add((byte)(0x80 | (run - 1)));
        x += run;
      }
    }

    // A record is counted in words, so an odd payload gets one byte of padding. The picture is
    // already full by then, so the reader never looks at it.
    if ((payload.Count & 1) != 0)
      payload.Add(0);

    return payload.ToArray();
  }

  private static void _WriteWord(byte[] data, int at, int value) {
    data[at] = (byte)(value >> 8);
    data[at + 1] = (byte)value;
  }
}
