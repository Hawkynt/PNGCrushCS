using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Ilbm;

namespace FileFormat.IffDpan;

/// <summary>Writes Deluxe Paint animations with a DPAN chunk in the first ILBM frame.</summary>
public static class IffDpanWriter {

  private const int _DPAN_PAYLOAD_SIZE = 8;
  private const int _DPAN_CHUNK_SIZE = 8 + _DPAN_PAYLOAD_SIZE;

  public static byte[] ToBytes(IffDpanFile file) {
    // Preserve an animation read from disk byte-for-byte. The image package exposes only the first
    // frame, so rebuilding a parsed multi-frame animation from that projection would discard data.
    if (file.RawData is { Length: > 0 } rawData)
      return rawData[..];

    if (file.Width <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), file.Width, "DPAN image width must be positive.");
    if (file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), file.Height, "DPAN image height must be positive.");

    var expectedPixelBytes = checked(file.Width * file.Height * 3);
    if (file.PixelData is not { } pixels || pixels.Length != expectedPixelBytes)
      throw new InvalidDataException($"DPAN RGB24 pixel data must contain exactly {expectedPixelBytes} bytes.");

    var rawImage = new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels[..],
    };

    var ilbmBytes = IlbmWriter.ToBytes(IlbmFile.FromRawImage(rawImage));
    var frameBytes = _InsertDpanChunk(ilbmBytes);
    var result = new byte[checked(12 + frameBytes.Length)];

    "FORM"u8.CopyTo(result);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), checked((uint)(result.Length - 8)));
    "ANIM"u8.CopyTo(result.AsSpan(8, 4));
    frameBytes.CopyTo(result.AsSpan(12));

    return result;
  }

  private static byte[] _InsertDpanChunk(ReadOnlySpan<byte> ilbmBytes) {
    if (ilbmBytes.Length < 12 || !ilbmBytes[..4].SequenceEqual("FORM"u8) || !ilbmBytes.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("ILBM writer returned an invalid FORM ILBM frame.");

    var formSize = BinaryPrimitives.ReadUInt32BigEndian(ilbmBytes.Slice(4, 4));
    if (formSize > int.MaxValue || formSize < 4 || 8L + formSize != ilbmBytes.Length)
      throw new InvalidDataException("ILBM writer returned an invalid FORM size.");

    var bodyOffset = _FindBodyOffset(ilbmBytes);
    var result = new byte[checked(ilbmBytes.Length + _DPAN_CHUNK_SIZE)];

    ilbmBytes[..bodyOffset].CopyTo(result);

    var dpan = result.AsSpan(bodyOffset, _DPAN_CHUNK_SIZE);
    "DPAN"u8.CopyTo(dpan);
    BinaryPrimitives.WriteUInt32BigEndian(dpan.Slice(4, 4), _DPAN_PAYLOAD_SIZE);
    BinaryPrimitives.WriteUInt16BigEndian(dpan.Slice(8, 2), IffDpanFile.CurrentVersion);
    BinaryPrimitives.WriteUInt16BigEndian(dpan.Slice(10, 2), 1);
    BinaryPrimitives.WriteUInt32BigEndian(dpan.Slice(12, 4), 0);

    ilbmBytes[bodyOffset..].CopyTo(result.AsSpan(bodyOffset + _DPAN_CHUNK_SIZE));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), checked((uint)(result.Length - 8)));

    return result;
  }

  private static int _FindBodyOffset(ReadOnlySpan<byte> ilbmBytes) {
    var end = ilbmBytes.Length;
    var offset = 12;

    while (offset < end) {
      if (offset + 8 > end)
        throw new InvalidDataException("ILBM frame ends inside a chunk header.");

      var size = BinaryPrimitives.ReadUInt32BigEndian(ilbmBytes.Slice(offset + 4, 4));
      if (size > int.MaxValue)
        throw new InvalidDataException("ILBM chunk is too large.");

      var dataSize = (int)size;
      var paddedSize = checked(dataSize + (dataSize & 1));
      var next = checked(offset + 8 + paddedSize);
      if (next > end)
        throw new InvalidDataException("ILBM frame ends inside a chunk payload.");

      if (ilbmBytes.Slice(offset, 4).SequenceEqual("BODY"u8))
        return offset;

      offset = next;
    }

    throw new InvalidDataException("ILBM writer returned a frame without a BODY chunk.");
  }
}
