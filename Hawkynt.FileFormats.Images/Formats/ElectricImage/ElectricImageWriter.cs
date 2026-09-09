using System;
using System.Collections.Generic;

namespace FileFormat.ElectricImage;

/// <summary>Writes ElectricImage pictures in the variants established by the measured corpus.</summary>
public static class ElectricImageWriter {

  private const ushort _ModeWithExtraFive = 0x0001;
  private const ushort _ModePlain = 0x0100;
  private const ushort _ExtraColour = 0x0100;
  private const ushort _ExtraAlpha = 0x0108;

  /// <summary>Serializes an ElectricImage picture.</summary>
  public static byte[] ToBytes(ElectricImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.Frames.Count is < 1 or > 4096)
      throw new ArgumentException($"An ElectricImage file needs between 1 and 4096 frames; this one has {file.Frames.Count}.", nameof(file));

    var output = new List<byte>();
    _Write16(output, ElectricImageFile.Version);
    _Write32(output, (uint)file.Frames.Count);

    foreach (var frame in file.Frames)
      _WriteFrame(output, frame);

    return output.ToArray();
  }

  private static void _WriteFrame(List<byte> output, ElectricImageFile.Frame frame) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width is < 1 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(frame), frame.Width, "An ElectricImage frame width must fit an unsigned 16-bit field and be non-zero.");
    if (frame.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(frame), frame.Height, "An ElectricImage frame height must fit an unsigned 16-bit field and be non-zero.");
    if (frame.BytesPerPixel is not (1 or 3 or 4))
      throw new ArgumentException($"ElectricImage writing supports one-byte indexed, three-byte RGB and four-byte ARGB frames; this frame has {frame.BytesPerPixel} bytes per pixel.", nameof(frame));

    var pixelBytes = checked(frame.Width * frame.Height * frame.BytesPerPixel);
    if (frame.PixelData.Length != pixelBytes)
      throw new ArgumentException($"The ElectricImage frame needs exactly {pixelBytes} bytes for {frame.Width}x{frame.Height} at {frame.BytesPerPixel} byte(s) per pixel, but carries {frame.PixelData.Length}.", nameof(frame));

    var lastPaletteIndex = frame.BytesPerPixel == 1 ? _ValidatePalette(frame) : -1;
    var packed = _Pack(frame.PixelData, frame.BytesPerPixel);
    var hasAlpha = frame.BytesPerPixel == 4;
    var indexed = frame.BytesPerPixel == 1;

    // The measured files repeat the dimensions in two places. The other fields here are zero in the
    // producer files and have no independently established semantics, so the writer does not invent any.
    _Write32(output, 0);
    _Write32(output, 0);
    _Write16(output, (ushort)frame.Height);
    _Write16(output, (ushort)frame.Width);
    output.Add((byte)(indexed ? 8 : 24));
    output.Add(0);
    _Write32(output, 0);
    _Write16(output, (ushort)frame.Height);
    _Write16(output, (ushort)frame.Width);
    _Write16(output, hasAlpha ? _ExtraAlpha : _ExtraColour);
    _Write32(output, (uint)packed.Length);
    _Write16(output, indexed ? _ModePlain : _ModeWithExtraFive);

    if (indexed) {
      output.Add(0);
      output.Add((byte)lastPaletteIndex);
      _Append(output, frame.Palette.AsSpan(0, checked((lastPaletteIndex + 1) * 3)));
    } else {
      // Mode 0001 carries five bytes between the frame header and its run-length stream in every
      // measured true-colour frame. Their meaning is unknown; all observed values are zero.
      output.AddRange([0, 0, 0, 0, 0]);
    }

    _Append(output, packed);
  }

  private static int _ValidatePalette(ElectricImageFile.Frame frame) {
    if (frame.Palette is null || frame.Palette.Length < 3)
      throw new ArgumentException("An indexed ElectricImage frame needs an RGB palette.", nameof(frame));
    if (frame.Palette.Length % 3 != 0 || frame.Palette.Length > 256 * 3)
      throw new ArgumentException("An ElectricImage palette must contain between 1 and 256 RGB triples.", nameof(frame));

    var last = 0;
    foreach (var index in frame.PixelData)
      if (index > last)
        last = index;

    if ((last + 1) * 3 > frame.Palette.Length)
      throw new ArgumentException($"The frame uses palette index {last}, but its palette contains only {frame.Palette.Length / 3} entries.", nameof(frame));

    return last;
  }

  /// <summary>
  /// Packs whole pixels. A lead below 80h repeats the following element one through 128 times; a
  /// lead at or above 80h is followed by one through 128 literal elements. Runs of two already win.
  /// </summary>
  private static byte[] _Pack(ReadOnlySpan<byte> data, int bytesPerPixel) {
    var elements = data.Length / bytesPerPixel;
    var output = new List<byte>(data.Length + (elements + 127) / 128);

    for (var at = 0; at < elements;) {
      var repeat = _RepeatLength(data, bytesPerPixel, at, elements);
      if (repeat >= 2) {
        output.Add((byte)(repeat - 1));
        _Append(output, data.Slice(at * bytesPerPixel, bytesPerPixel));
        at += repeat;
        continue;
      }

      var literalStart = at++;
      while (at < elements && at - literalStart < 128 && _RepeatLength(data, bytesPerPixel, at, elements) < 2)
        ++at;

      var literalCount = at - literalStart;
      output.Add((byte)(0x80 | (literalCount - 1)));
      _Append(output, data.Slice(literalStart * bytesPerPixel, literalCount * bytesPerPixel));
    }

    return output.ToArray();
  }

  private static int _RepeatLength(ReadOnlySpan<byte> data, int bytesPerPixel, int at, int elements) {
    var length = 1;
    var sample = data.Slice(at * bytesPerPixel, bytesPerPixel);
    while (length < 128 && at + length < elements
           && sample.SequenceEqual(data.Slice((at + length) * bytesPerPixel, bytesPerPixel)))
      ++length;

    return length;
  }

  private static void _Write16(List<byte> output, ushort value) {
    output.Add((byte)(value >> 8));
    output.Add((byte)value);
  }

  private static void _Write32(List<byte> output, uint value) {
    output.Add((byte)(value >> 24));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 8));
    output.Add((byte)value);
  }

  private static void _Append(List<byte> output, ReadOnlySpan<byte> data) {
    foreach (var value in data)
      output.Add(value);
  }
}
