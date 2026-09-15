using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedDib;
using FileFormat.Wrappers;

namespace FileFormat.Cmx;

/// <summary>Writes a CMX 5 / 16-bit RIFF document from a raster image.</summary>
/// <remarks>
/// CMX is a metafile rather than a bitmap wrapper. Arbitrary pixels are therefore represented by
/// ordinary filled rectangle primitives on a real CMX page, with an RGB description table and the
/// page/master indexes needed to find it. The vector scene is bounded to 128 samples on its longest
/// side and 256 colours so pathological raster input does not explode into millions of metafile
/// records. A full-resolution <c>DISP</c> bitmap is written alongside it as the document thumbnail.
/// </remarks>
public static class CmxWriter {
  private const int _MaximumVectorDimension = 128;
  private const int _MaximumVectorColors = 256;
  private const int _MaximumCoordinate = 30000;

  private const ushort _MasterIndexTable = 1;
  private const ushort _PageIndexTable = 2;
  private const ushort _ColorDescriptionSection = 21;

  private const short _CommandBeginPage = 9;
  private const short _CommandEndPage = 10;
  private const short _CommandRectangle = 68;

  /// <summary>Serializes <paramref name="file"/> as a little-endian CMX 5 document.</summary>
  public static byte[] ToBytes(CmxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return ToBytes(file.Preview ?? throw new ArgumentException("No image is available to write.", nameof(file)));
  }

  /// <summary>Serializes arbitrary raster input as a CMX page plus a full-resolution thumbnail.</summary>
  public static byte[] ToBytes(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (!image.HasEnoughPixelData)
      throw new ArgumentException("The source image has invalid dimensions or too little pixel data.", nameof(image));

    var vector = _Vectorize(image);
    var page = _BuildPage(vector);
    var colors = _BuildColorDescription(vector);
    var display = _BuildDisplay(image);

    const int riffHeaderSize = 12;
    const int containerHeaderPayloadSize = 104;
    var containerChunkSize = _ChunkSize(containerHeaderPayloadSize);

    var pageOffset = riffHeaderSize + containerChunkSize;
    var colorOffset = pageOffset + _ChunkSize(page.Length);
    var displayOffset = colorOffset + _ChunkSize(colors.Length);
    var pageIndexOffset = displayOffset + _ChunkSize(display.Length);

    var pageIndex = _BuildPageIndex(pageOffset);
    var masterIndexOffset = pageIndexOffset + _ChunkSize(pageIndex.Length);
    var masterIndex = _BuildMasterIndex(colorOffset, pageIndexOffset);
    var containerHeader = _BuildContainerHeader(masterIndexOffset, displayOffset, vector.PageWidth, vector.PageHeight);

    var totalLength = checked(
      riffHeaderSize
      + _ChunkSize(containerHeader.Length)
      + _ChunkSize(page.Length)
      + _ChunkSize(colors.Length)
      + _ChunkSize(display.Length)
      + _ChunkSize(pageIndex.Length)
      + _ChunkSize(masterIndex.Length));

    var result = new byte[totalLength];
    _Ascii4("RIFF", result);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
    _Ascii4("CMX1", result.AsSpan(8));

    var offset = riffHeaderSize;
    offset = _WriteChunk(result, offset, "cont", containerHeader);
    offset = _WriteChunk(result, offset, "page", page);
    offset = _WriteChunk(result, offset, "rclr", colors);
    offset = _WriteChunk(result, offset, "DISP", display);
    offset = _WriteChunk(result, offset, "ixpg", pageIndex);
    offset = _WriteChunk(result, offset, "ixmr", masterIndex);

    if (offset != result.Length)
      throw new InvalidOperationException("CMX size accounting did not consume the output buffer exactly.");

    return result;
  }

  private static _VectorImage _Vectorize(RawImage image) {
    var longest = Math.Max(image.Width, image.Height);
    var width = image.Width;
    var height = image.Height;
    if (longest > _MaximumVectorDimension) {
      width = Math.Max(1, checked((int)Math.Round((double)image.Width * _MaximumVectorDimension / longest)));
      height = Math.Max(1, checked((int)Math.Round((double)image.Height * _MaximumVectorDimension / longest)));
    }

    var sampled = width == image.Width && height == image.Height ? image : image.SampleTo(width, height);
    var bgra = sampled.EnsureFormat(PixelFormat.Bgra32);
    var opaqueBytes = bgra.PixelData[..];

    for (var i = 0; i < opaqueBytes.Length; i += 4) {
      var alpha = opaqueBytes[i + 3];
      if (alpha != byte.MaxValue) {
        opaqueBytes[i] = _CompositeOverWhite(opaqueBytes[i], alpha);
        opaqueBytes[i + 1] = _CompositeOverWhite(opaqueBytes[i + 1], alpha);
        opaqueBytes[i + 2] = _CompositeOverWhite(opaqueBytes[i + 2], alpha);
        opaqueBytes[i + 3] = byte.MaxValue;
      }
    }

    var opaque = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Bgra32,
      PixelData = opaqueBytes,
    };
    var indexed = opaque.EnsureFormat(PixelFormat.Indexed8);
    if (indexed.Palette is null || indexed.PaletteCount is < 1 or > _MaximumVectorColors)
      throw new InvalidOperationException("The indexed CMX scene has no usable RGB palette.");

    var scale = Math.Max(2, _MaximumCoordinate / Math.Max(width, height));
    scale &= ~1;
    if (scale < 2)
      scale = 2;

    return new(
      width,
      height,
      scale,
      checked(width * scale),
      checked(height * scale),
      indexed.PixelData,
      indexed.Palette,
      indexed.PaletteCount
    );
  }

  private static byte _CompositeOverWhite(byte component, byte alpha)
    => checked((byte)((component * alpha + byte.MaxValue * (byte.MaxValue - alpha) + 127) / byte.MaxValue));

  private static byte[] _BuildPage(_VectorImage image) {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    _WriteInstruction(writer, _CommandBeginPage, static (w, state) => {
      w.Write((ushort)0); // reserved
      w.Write(0u); // flags
      w.Write((short)0);
      w.Write((short)0);
      w.Write(checked((short)state.PageWidth));
      w.Write(checked((short)state.PageHeight));
    }, image);

    for (var y = 0; y < image.Height; ++y) {
      var row = y * image.Width;
      for (var x = 0; x < image.Width;) {
        var color = image.Indices[row + x];
        var end = x + 1;
        while (end < image.Width && image.Indices[row + end] == color)
          ++end;

        var runStart = x;
        var runLength = end - x;
        _WriteInstruction(writer, _CommandRectangle, static (w, state) => {
          var (vector, start, length, rowIndex, paletteIndex) = state;
          var width = checked(length * vector.Scale);
          var centerX = checked(start * vector.Scale + width / 2);
          var centerY = checked(vector.PageHeight - rowIndex * vector.Scale - vector.Scale / 2);

          w.Write((byte)0x01); // rendering attributes: fill only
          w.Write((ushort)1); // uniform fill
          w.Write(checked((ushort)(paletteIndex + 1))); // CMX colour references are one-based
          w.Write((ushort)0); // screen reference
          w.Write(checked((short)centerX));
          w.Write(checked((short)centerY));
          w.Write(checked((short)width));
          w.Write(checked((short)vector.Scale));
          w.Write((short)0); // corner radius
          w.Write((short)0); // angle, tenths of a degree
        }, (image, runStart, runLength, y, (int)color));

        x = end;
      }
    }

    _WriteInstruction(writer, _CommandEndPage, static (_, _) => { }, 0);
    return stream.ToArray();
  }

  private static byte[] _BuildColorDescription(_VectorImage image) {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(checked((ushort)image.PaletteCount));

    for (var i = 0; i < image.PaletteCount; ++i) {
      var offset = i * 3;
      writer.Write((byte)5); // RGB
      writer.Write((byte)0); // palette/type selector
      writer.Write(image.Palette[offset]);
      writer.Write(image.Palette[offset + 1]);
      writer.Write(image.Palette[offset + 2]);
    }

    return stream.ToArray();
  }

  private static byte[] _BuildDisplay(RawImage image) {
    var dib = EmbeddedDibWriter.ToBytes(image);
    if (dib.Length < WrappedDib.MinHeaderSize)
      throw new InvalidOperationException("The embedded-DIB writer produced no usable bitmap header.");

    var result = new byte[checked(sizeof(uint) + dib.Length)];
    var pixelOffset = checked((uint)(14 + WrappedDib.PixelOffset(dib, 0)));
    BinaryPrimitives.WriteUInt32LittleEndian(result, pixelOffset);
    dib.CopyTo(result.AsSpan(sizeof(uint)));
    return result;
  }

  private static byte[] _BuildPageIndex(int pageOffset) {
    var result = new byte[2 + 4 * sizeof(uint)];
    BinaryPrimitives.WriteUInt16LittleEndian(result, 1);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2), checked((uint)pageOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(6), uint.MaxValue);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(10), uint.MaxValue);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), uint.MaxValue);
    return result;
  }

  private static byte[] _BuildMasterIndex(int colorOffset, int pageIndexOffset) {
    var result = new byte[6 + 2 * 6];
    BinaryPrimitives.WriteUInt16LittleEndian(result, _MasterIndexTable);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 6);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), _ColorDescriptionSection);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)colorOffset));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), _PageIndexTable);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), checked((uint)pageIndexOffset));
    return result;
  }

  private static byte[] _BuildContainerHeader(int masterIndexOffset, int displayOffset, int pageWidth, int pageHeight) {
    var result = new byte[104];
    _AsciiPadded("Corel Binary Metafile", result.AsSpan(0, 32));
    _AsciiPadded("Windows", result.AsSpan(32, 16));
    _AsciiPadded("2", result.AsSpan(48, 4)); // Intel/little-endian byte order
    _AsciiPadded("2", result.AsSpan(52, 2)); // two-byte coordinates
    _AsciiPadded("1", result.AsSpan(54, 4)); // CMX 5 / 16-bit internal version
    _AsciiPadded("0", result.AsSpan(58, 4));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(62), 0);
    BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(64), BitConverter.DoubleToInt64Bits(1.0));

    // Twelve reserved bytes begin at 72. The three absolute section offsets follow them.
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(84), checked((uint)masterIndexOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(88), uint.MaxValue);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(92), checked((uint)displayOffset));

    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(96), 0);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(98), 0);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(100), checked((short)pageWidth));
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(102), checked((short)pageHeight));
    return result;
  }

  private static void _WriteInstruction<TState>(BinaryWriter writer, short code, Action<BinaryWriter, TState> writePayload, TState state) {
    using var payload = new MemoryStream();
    using (var payloadWriter = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
      writePayload(payloadWriter, state);

    var size = checked((short)(4 + payload.Length));
    writer.Write(size);
    writer.Write(code);
    writer.Write(payload.GetBuffer(), 0, checked((int)payload.Length));
  }

  private static int _ChunkSize(int payloadLength) => checked(8 + payloadLength + (payloadLength & 1));

  private static int _WriteChunk(byte[] destination, int offset, string id, ReadOnlySpan<byte> payload) {
    _Ascii4(id, destination.AsSpan(offset));
    BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 4), checked((uint)payload.Length));
    payload.CopyTo(destination.AsSpan(offset + 8));
    return checked(offset + _ChunkSize(payload.Length));
  }

  private static void _Ascii4(string value, Span<byte> destination) {
    if (value.Length != 4)
      throw new ArgumentException("A RIFF FourCC must contain exactly four ASCII characters.", nameof(value));
    for (var i = 0; i < 4; ++i)
      destination[i] = checked((byte)value[i]);
  }

  private static void _AsciiPadded(string value, Span<byte> destination) {
    destination.Clear();
    var count = Math.Min(value.Length, destination.Length);
    for (var i = 0; i < count; ++i)
      destination[i] = checked((byte)value[i]);
  }

  private sealed record _VectorImage(
    int Width,
    int Height,
    int Scale,
    int PageWidth,
    int PageHeight,
    byte[] Indices,
    byte[] Palette,
    int PaletteCount
  );
}
