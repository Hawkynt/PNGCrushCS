using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.DigitalFx;

/// <summary>Writes Digital F/X pictures (.tdim).</summary>
public static class DigitalFxWriter {

  public static byte[] ToBytes(DigitalFxFile file) {
    if (file.Width is < 1 or > DigitalFxFile.MaximumSide || file.Height is < 1 or > DigitalFxFile.MaximumSide)
      throw new ArgumentOutOfRangeException(nameof(file), $"Digital F/X dimensions must be between 1 and {DigitalFxFile.MaximumSide} pixels per side.");

    var count = checked(file.Width * file.Height);
    var required = checked(count * DigitalFxFile.BytesPerPixel);
    if (file.PixelData == null || file.PixelData.Length < required)
      throw new ArgumentException("The Digital F/X image does not contain enough pixel data for its dimensions.", nameof(file));

    var output = new List<byte>(16 + required + (count + 127) / 128);
    output.AddRange(DigitalFxFile.Magic.ToArray());
    output.AddRange([0, 0, 0, 0]);

    Span<byte> headerTail = stackalloc byte[8];
    BinaryPrimitives.WriteUInt16BigEndian(headerTail, checked((ushort)file.Height));
    BinaryPrimitives.WriteUInt16BigEndian(headerTail[2..], checked((ushort)file.Width));
    BinaryPrimitives.WriteUInt32BigEndian(headerTail[4..], 16);
    output.AddRange(headerTail.ToArray());

    var pixels = file.PixelData.AsSpan(0, required);
    var at = 0;
    while (at < count) {
      var repeated = _RepeatedRunLength(pixels, at, count);
      if (repeated >= 2) {
        output.Add((byte)(repeated - 1));
        _AppendPixel(output, pixels, at);
        at += repeated;
        continue;
      }

      var literalStart = at++;
      while (at < count && at - literalStart < 128) {
        if (_RepeatedRunLength(pixels, at, count) >= 2)
          break;
        ++at;
      }

      var literalCount = at - literalStart;
      output.Add((byte)(0x80 | (literalCount - 1)));
      var byteStart = literalStart * DigitalFxFile.BytesPerPixel;
      var byteCount = literalCount * DigitalFxFile.BytesPerPixel;
      output.AddRange(pixels.Slice(byteStart, byteCount).ToArray());
    }

    return output.ToArray();
  }

  private static int _RepeatedRunLength(ReadOnlySpan<byte> pixels, int start, int count) {
    var run = 1;
    while (run < 128 && start + run < count && _PixelsEqual(pixels, start, start + run))
      ++run;
    return run;
  }

  private static bool _PixelsEqual(ReadOnlySpan<byte> pixels, int a, int b) {
    var offsetA = a * DigitalFxFile.BytesPerPixel;
    var offsetB = b * DigitalFxFile.BytesPerPixel;
    return pixels.Slice(offsetA, DigitalFxFile.BytesPerPixel)
      .SequenceEqual(pixels.Slice(offsetB, DigitalFxFile.BytesPerPixel));
  }

  private static void _AppendPixel(List<byte> output, ReadOnlySpan<byte> pixels, int index) {
    var offset = index * DigitalFxFile.BytesPerPixel;
    for (var i = 0; i < DigitalFxFile.BytesPerPixel; ++i)
      output.Add(pixels[offset + i]);
  }
}
