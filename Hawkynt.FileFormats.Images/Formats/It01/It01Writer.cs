using System;
using System.Buffers.Binary;

namespace FileFormat.It01;

/// <summary>Assembles an "IT01" picture, in the shape the one known sample has.</summary>
/// <remarks>
/// The fields between the size and the data offset are copied from that sample rather than
/// understood, so this writes what is known to be read rather than claiming to know the format.
/// </remarks>
public static class It01Writer {

  /// <summary>The header words the sample carries between its size and its data offset.</summary>
  private static ReadOnlySpan<int> _SampleTail => [1, 3, 2, 1, 1, 2, 128, 128, 1, 3];

  public static byte[] ToBytes(It01File file) {
    var bands = file.Bands is 1 or 3 ? file.Bands : 3;
    var pixels = file.PixelData ?? [];
    var needed = file.Width * file.Height * bands;

    var result = new byte[It01File.DefaultDataOffset + needed];
    It01File.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(It01File.WidthAt), file.Width);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(It01File.HeightAt), file.Height);

    for (var i = 0; i < _SampleTail.Length; ++i)
      BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(12 + i * 4), _SampleTail[i]);

    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(It01File.BandsAt), bands);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(It01File.DataOffsetAt), It01File.DefaultDataOffset);

    pixels.AsSpan(0, Math.Min(pixels.Length, needed)).CopyTo(result.AsSpan(It01File.DefaultDataOffset));

    return result;
  }
}
