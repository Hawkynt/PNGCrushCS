using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.EmbeddedDib;
using FileFormat.Riff;
using FileFormat.Wrappers;

namespace FileFormat.Cdr;

/// <summary>Writes RIFF-era CorelDRAW containers and raster-backed CorelDRAW 4 documents.</summary>
public static class CdrWriter {
  private const ushort _Cdr4Version = 400;
  private const ushort _BitmapId = 1;
  private const int _TargetPageDimension = 10_000;

  /// <summary>
  /// Authors a CorelDRAW 4 page containing one bitmap object that covers the page and a matching
  /// <c>DISP</c> preview. CDR4 is used because its bitmap record stores a complete standard BMP.
  /// </summary>
  public static CdrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > CdrFile.MaxDimension)
      throw new ArgumentOutOfRangeException(nameof(image), $"CDR width must be between 1 and {CdrFile.MaxDimension} pixels.");
    if (image.Height is < 1 or > CdrFile.MaxDimension)
      throw new ArgumentOutOfRangeException(nameof(image), $"CDR height must be between 1 and {CdrFile.MaxDimension} pixels.");

    var scale = Math.Max(1, Math.Min(1000, _TargetPageDimension / Math.Max(image.Width, image.Height)));
    var pageWidth = checked((short)(image.Width * scale));
    var pageHeight = checked((short)(image.Height * scale));
    var bitmap = BmpWriter.ToBytes(BmpFile.FromRawImage(image));

    return new CdrFile {
      FormType = "CDR4",
      Version = _Cdr4Version,
      Preview = image,
      Chunks = [
        new RiffChunk { Id = "vrsn", Data = _UInt16(_Cdr4Version) },
        new RiffChunk { Id = "DISP", Data = _BuildDisp(image) },
        new RiffChunk { Id = "mcfg", Data = _BuildPageConfiguration(pageWidth, pageHeight) },
        new RiffChunk { Id = "bmp ", Data = _BuildBitmap(bitmap) },
        new RiffChunk { Id = "LIST", Data = _BuildPage(pageWidth, pageHeight) },
      ],
    };
  }

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
    if (!_IsCdrForm(file.FormType))
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

    // Keep parsed Corel LIST chunks opaque: putting them in RiffFile.Lists would recursively reinterpret
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

  private static byte[] _BuildPageConfiguration(short width, short height) {
    var result = new byte[4];
    BinaryPrimitives.WriteInt16LittleEndian(result, width);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(2), height);
    return result;
  }

  private static byte[] _BuildBitmap(ReadOnlySpan<byte> bitmap) {
    var result = new byte[checked(sizeof(ushort) + bitmap.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(result, _BitmapId);
    bitmap.CopyTo(result.AsSpan(sizeof(ushort)));
    return result;
  }

  private static byte[] _BuildPage(short width, short height) {
    var transform = new RiffChunk { Id = "trfd", Data = _BuildTransform(width / 2, height / 2) };
    var objectData = new RiffChunk { Id = "loda", Data = _BuildBitmapObject(checked((short)-width), checked((short)-height)) };
    var objectList = new RiffChunk { Id = "LIST", Data = _BuildListPayload("obj ", transform, objectData) };

    return _BuildListPayload(
      "page",
      new RiffChunk { Id = "flgs", Data = new byte[sizeof(uint)] },
      objectList
    );
  }

  private static byte[] _BuildTransform(int x, int y) {
    const ushort transformType = 0x08;
    var result = new byte[34];
    BinaryPrimitives.WriteUInt16LittleEndian(result, checked((ushort)result.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 1); // number of arguments
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 6); // offset-table start
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 8); // transform argument offset
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), transformType);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(10), 0x0001_0000); // 1.0 fixed point
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), 0);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(18), x);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(22), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(26), 0x0001_0000); // 1.0 fixed point
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(30), y);
    return result;
  }

  private static byte[] _BuildBitmapObject(short width, short height) {
    const ushort bitmapChunkType = 0x05;
    const ushort bitmapArgumentType = 0x1E;
    var result = new byte[48];
    BinaryPrimitives.WriteUInt16LittleEndian(result, checked((ushort)result.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 1); // number of arguments
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 10); // argument-offset table
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 12); // argument-type table
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), bitmapChunkType);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), 14); // bitmap argument offset
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), bitmapArgumentType);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(14), width);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(16), height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(26), _BitmapId);
    return result;
  }

  private static byte[] _BuildListPayload(FourCC listType, params RiffChunk[] chunks) {
    var riff = RiffWriter.ToBytes(new RiffFile { FormType = listType, Chunks = [.. chunks] });
    return riff[(RiffHeader.StructSize - sizeof(uint))..];
  }

  private static byte[] _UInt16(ushort value) {
    var result = new byte[sizeof(ushort)];
    BinaryPrimitives.WriteUInt16LittleEndian(result, value);
    return result;
  }

  private static bool _IsCdrForm(FourCC form)
    => form is { A: (byte)'C', B: (byte)'D', C: (byte)'R' }
      or { A: (byte)'c', B: (byte)'d', C: (byte)'r', D: (byte)'8' };
}
