using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.EmbeddedDib;
using FileFormat.Riff;
using FileFormat.Wrappers;

namespace FileFormat.Cdr;

/// <summary>Re-serializes RIFF-era CorelDRAW containers and can replace their <c>DISP</c> preview.</summary>
public static class CdrWriter {

  /// <summary>Writes the parsed container without interpreting its opaque Corel payloads.</summary>
  public static byte[] ToBytes(CdrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Write(file, null);
  }

  /// <summary>Writes the container while replacing, or adding, its first <c>DISP</c> preview.</summary>
  public static byte[] ToBytes(CdrFile file, RawImage preview) {
    ArgumentNullException.ThrowIfNull(file);
    ArgumentNullException.ThrowIfNull(preview);
    return _Write(file, preview);
  }

  private static byte[] _Write(CdrFile file, RawImage? preview) {
    if (file.FormType.A != (byte)'C' || file.FormType.B != (byte)'D' || file.FormType.C != (byte)'R')
      throw new ArgumentException($"RIFF form {file.FormType} is not a CorelDRAW CDR form.", nameof(file));

    var chunks = new List<RiffChunk>(file.Chunks.Count + (preview is null ? 0 : 1));
    var replaced = false;

    foreach (var chunk in file.Chunks) {
      if (!replaced && preview is not null && chunk.Id.ToString() == "DISP") {
        chunks.Add(new RiffChunk { Id = chunk.Id, Data = _BuildDisp(preview) });
        replaced = true;
      } else
        chunks.Add(new RiffChunk { Id = chunk.Id, Data = chunk.Data });
    }

    if (preview is not null && !replaced) {
      var insertAt = chunks.FindIndex(static chunk => chunk.Id.ToString() == "vrsn");
      insertAt = insertAt < 0 ? 0 : insertAt + 1;
      chunks.Insert(insertAt, new RiffChunk { Id = "DISP", Data = _BuildDisp(preview) });
    }

    // Keep Corel LIST chunks opaque: putting them in RiffFile.Lists would recursively reinterpret
    // LIST cmpr as ordinary RIFF children, which it is not.
    var riff = RiffWriter.ToBytes(new RiffFile { FormType = file.FormType, Chunks = chunks });
    if (file.TrailingData.Length == 0)
      return riff;

    var result = new byte[checked(riff.Length + file.TrailingData.Length)];
    riff.CopyTo(result, 0);
    file.TrailingData.CopyTo(result, riff.Length);
    return result;
  }

  private static byte[] _BuildDisp(RawImage preview) {
    var dib = EmbeddedDibWriter.ToBytes(preview);
    if (dib.Length < WrappedDib.MinHeaderSize)
      throw new InvalidOperationException("The embedded-DIB writer produced no usable bitmap header.");

    var pixelOffset = checked((uint)(14 + WrappedDib.PixelOffset(dib, 0)));
    var result = new byte[checked(sizeof(uint) + dib.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(result, pixelOffset);
    dib.CopyTo(result.AsSpan(sizeof(uint)));
    return result;
  }
}
