using System;
using System.Buffers.Binary;

namespace FileFormat.Mrw;

/// <summary>Writes Minolta MRW files using the packed 12-bit RGGB layout used by later cameras.</summary>
public static class MrwWriter {

  private const int _PictureDataSize = 24;
  private const int _WhiteBalanceDataSize = 12;
  private const int _SensorOffset = 512;
  private const byte _PackedStorage = 0x59;
  private const ushort _RggbPattern = 0x0001;
  private const byte _UnityDenominatorExponent = 2;
  private const ushort _UnityNumerator = 256;

  private static ReadOnlySpan<byte> _PaddingBlock => [0x00, (byte)'P', (byte)'A', (byte)'D'];

  public static byte[] ToBytes(MrwFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);

    if (file.Width is < 1 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file.Width), file.Width, "MRW width must fit in an unsigned 16-bit field.");
    if (file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file.Height), file.Height, "MRW height must fit in an unsigned 16-bit field.");
    if ((file.Width & 1) != 0)
      throw new ArgumentException("Packed MRW stores each scanline as pairs of 12-bit samples, so the width must be even.", nameof(file));

    var pixelCount = (long)file.Width * file.Height;
    var expectedRgbBytes = pixelCount * 3;
    if (file.PixelData.LongLength != expectedRgbBytes)
      throw new ArgumentException(
        $"An {file.Width} by {file.Height} RGB24 image needs {expectedRgbBytes} bytes, but the MRW file carries {file.PixelData.LongLength}.",
        nameof(file));

    var sensorBytes = pixelCount / 2 * 3;
    var fileSize = _SensorOffset + sensorBytes;
    if (fileSize > Array.MaxLength)
      throw new ArgumentException($"The encoded MRW would require {fileSize} bytes, which is too large for one managed byte array.", nameof(file));

    var result = new byte[(int)fileSize];
    var target = result.AsSpan();

    MrwFile.Magic.CopyTo(target);
    BinaryPrimitives.WriteUInt32BigEndian(target[4..], (uint)(_SensorOffset - MrwFile.HeaderSize));

    Span<byte> picture = stackalloc byte[_PictureDataSize];
    "00000000"u8.CopyTo(picture);
    BinaryPrimitives.WriteUInt16BigEndian(picture[8..], (ushort)file.Height);
    BinaryPrimitives.WriteUInt16BigEndian(picture[10..], (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(picture[12..], (ushort)file.Height);
    BinaryPrimitives.WriteUInt16BigEndian(picture[14..], (ushort)file.Width);
    picture[16] = (byte)MrwFile.SupportedBitsPerSample;
    picture[17] = (byte)MrwFile.SupportedBitsPerSample;
    picture[18] = _PackedStorage;
    BinaryPrimitives.WriteUInt16BigEndian(picture[22..], _RggbPattern);

    Span<byte> whiteBalance = stackalloc byte[_WhiteBalanceDataSize];
    whiteBalance[..4].Fill(_UnityDenominatorExponent);
    for (var offset = 4; offset < whiteBalance.Length; offset += 2)
      BinaryPrimitives.WriteUInt16BigEndian(whiteBalance[offset..], _UnityNumerator);

    var at = MrwFile.HeaderSize;
    _WriteBlock(target, ref at, MrwFile.PictureBlock, picture);
    _WriteBlock(target, ref at, MrwFile.WhiteBalanceBlock, whiteBalance);

    var paddingLength = _SensorOffset - at - MrwFile.BlockHeaderSize;
    _WriteBlockHeader(target, ref at, _PaddingBlock, paddingLength);
    at += paddingLength;

    _WriteSensor(target[at..], file.PixelData, file.Width, file.Height);
    return result;
  }

  private static void _WriteBlock(Span<byte> target, ref int at, ReadOnlySpan<byte> name, ReadOnlySpan<byte> data) {
    _WriteBlockHeader(target, ref at, name, data.Length);
    data.CopyTo(target[at..]);
    at += data.Length;
  }

  private static void _WriteBlockHeader(Span<byte> target, ref int at, ReadOnlySpan<byte> name, int length) {
    name.CopyTo(target[at..]);
    BinaryPrimitives.WriteUInt32BigEndian(target[(at + 4)..], (uint)length);
    at += MrwFile.BlockHeaderSize;
  }

  private static void _WriteSensor(Span<byte> target, ReadOnlySpan<byte> rgb, int width, int height) {
    var output = 0;
    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; x += 2) {
        var first = _Sample(rgb, width, x, y);
        var second = _Sample(rgb, width, x + 1, y);

        target[output++] = (byte)(first >> 4);
        target[output++] = (byte)(((first & 0x000F) << 4) | (second >> 8));
        target[output++] = (byte)second;
      }
    }
  }

  private static ushort _Sample(ReadOnlySpan<byte> rgb, int width, int x, int y) {
    var pixelAt = (y * width + x) * 3;
    var channel = ((y & 1, x & 1)) switch {
      (0, 0) => 0,
      (1, 1) => 2,
      _ => 1,
    };
    var value = rgb[pixelAt + channel];
    return (ushort)((value << 4) | (value >> 4));
  }
}
