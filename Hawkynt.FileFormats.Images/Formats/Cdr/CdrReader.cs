using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Riff;
using FileFormat.Wrappers;

namespace FileFormat.Cdr;

/// <summary>Reads the RIFF-based generations of CorelDRAW documents.</summary>
public static class CdrReader {

  public static CdrFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < RiffHeader.StructSize)
      throw new InvalidDataException("Data is too small to contain a CorelDRAW RIFF header.");

    var header = RiffHeader.ReadFrom(data);
    if (header.ChunkId.ToString() != "RIFF")
      throw new InvalidDataException("CorelDRAW RIFF data must start with RIFF.");
    if (!_IsCdrForm(header.FormType))
      throw new InvalidDataException($"RIFF form {header.FormType} is not a CorelDRAW CDR form.");
    if (header.Size < 4)
      throw new InvalidDataException("The CorelDRAW RIFF size does not include its form type.");

    var declaredEnd = 8L + header.Size;
    if (declaredEnd > data.Length)
      throw new InvalidDataException("The CorelDRAW RIFF extent runs past the available data.");

    var end = checked((int)declaredEnd);
    var chunks = new List<RiffChunk>();
    RawImage? preview = null;
    var version = 0;
    var offset = RiffHeader.StructSize;

    while (offset < end) {
      if (end - offset < RiffChunkHeader.StructSize)
        throw new InvalidDataException("The CorelDRAW RIFF ends inside a chunk header.");

      var chunkHeader = RiffChunkHeader.ReadFrom(data[offset..]);
      var payloadStart = offset + RiffChunkHeader.StructSize;
      var payloadEnd = (long)payloadStart + chunkHeader.Size;
      var next = payloadEnd + (chunkHeader.Size & 1);
      if (payloadEnd > end || next > end)
        throw new InvalidDataException($"CorelDRAW chunk {chunkHeader.ChunkId} runs past the RIFF extent.");

      var payloadLength = checked((int)chunkHeader.Size);
      var payload = data.Slice(payloadStart, payloadLength).ToArray();
      chunks.Add(new RiffChunk { Id = chunkHeader.ChunkId, Data = payload });

      switch (chunkHeader.ChunkId.ToString()) {
        case "vrsn" when version == 0 && payload.Length >= sizeof(ushort):
          version = BinaryPrimitives.ReadUInt16LittleEndian(payload);
          break;
        case "DISP" when preview is null:
          preview = _TryDecodeDisp(payload);
          break;
      }

      offset = checked((int)next);
    }

    if (version == 0)
      version = _VersionFromForm(header.FormType);

    return new CdrFile {
      FormType = header.FormType,
      Version = version,
      Chunks = chunks,
      Preview = preview,
      TrailingData = data[end..].ToArray(),
    };
  }

  private static RawImage? _TryDecodeDisp(ReadOnlySpan<byte> payload) {
    // Corel's DISP begins with the four bytes that a BMP stores at file-header offset 10
    // (bfOffBits); the packed DIB follows immediately after them.
    if (payload.Length < sizeof(uint) + WrappedDib.MinHeaderSize)
      return null;

    var statedPixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(payload);
    var dib = payload[sizeof(uint)..];
    if (WrappedDib.Measure(dib, 0, CdrFile.MaxDimension) < 0)
      return null;

    var expectedPixelOffset = checked((uint)(14 + WrappedDib.PixelOffset(dib, 0)));
    if (statedPixelOffset != expectedPixelOffset)
      return null;

    try {
      return WrappedDib.Decode(dib, 0, CdrFile.MaxDimension, "CorelDRAW DISP preview");
    } catch (Exception exception) when (exception is InvalidDataException or ArgumentException or NotSupportedException) {
      // A broken thumbnail does not make the surrounding vector document unreadable.
      return null;
    }
  }

  private static bool _IsCdrForm(FourCC form)
    => form is { A: (byte)'C', B: (byte)'D', C: (byte)'R' }
      or { A: (byte)'c', B: (byte)'d', C: (byte)'r' };

  private static int _VersionFromForm(FourCC form) {
    if (form is { A: (byte)'c', B: (byte)'d', C: (byte)'r', D: (byte)'8' })
      return 801;

    return form.D switch {
      (byte)' ' => 300,
      >= (byte)'1' and <= (byte)'9' => (form.D - (byte)'0') * 100,
      >= (byte)'A' and <= (byte)'H' => (form.D - 0x37) * 100,
      (byte)'I' => 0,
      >= (byte)'J' and <= (byte)'Z' => (form.D - 0x38) * 100,
      _ => 0,
    };
  }
}
