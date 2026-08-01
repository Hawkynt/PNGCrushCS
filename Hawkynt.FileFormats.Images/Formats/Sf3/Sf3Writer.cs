using System;

namespace FileFormat.Sf3;

/// <summary>Assembles an SF3 image from an <see cref="Sf3File"/>.</summary>
public static class Sf3Writer {

  public static byte[] ToBytes(Sf3File file) {
    var samples = file.Samples ?? [];
    var count = file.Width * file.Height * file.Channels;
    var result = new byte[Sf3File.HeaderSize + count];

    Sf3File.Signature.CopyTo(result);
    result[Sf3File.Signature.Length] = Sf3File.ImageFormatId;

    // Bytes 11 to 14 are a checksum no reader verifies, so they are left zero rather than guessed.
    _WriteInt32(result, Sf3File.WidthOffset, file.Width);
    _WriteInt32(result, Sf3File.WidthOffset + 4, file.Height);
    _WriteInt32(result, Sf3File.WidthOffset + 8, 1);
    result[Sf3File.ChannelsOffset] = (byte)file.Channels;

    // High nibble names the family, low nibble the bytes a sample takes.
    result[Sf3File.SampleFormatOffset] = 0x11;

    samples.AsSpan(0, Math.Min(samples.Length, count)).CopyTo(result.AsSpan(Sf3File.HeaderSize));

    return result;
  }

  private static void _WriteInt32(Span<byte> data, int offset, int value) {
    data[offset] = (byte)value;
    data[offset + 1] = (byte)(value >> 8);
    data[offset + 2] = (byte)(value >> 16);
    data[offset + 3] = (byte)(value >> 24);
  }
}
