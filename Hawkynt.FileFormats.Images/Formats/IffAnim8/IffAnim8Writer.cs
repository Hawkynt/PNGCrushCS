using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffAnim8;

/// <summary>Assembles IFF ANIM method-8 bytes from an <see cref="IffAnim8File"/>.</summary>
public static class IffAnim8Writer {

  private const int _AnhdSize = 40;
  private const int _DltaPointerCount = 16;
  private const int _DltaPlanePointerCount = 8;
  private const int _DltaPointerTableSize = _DltaPointerCount * sizeof(uint);
  private const int _MaximumUniqueOrSkipCount = 0x7FFF;

  /// <summary>
  /// Writes a single-frame ANIM whose first ILBM frame is encoded as an operation-8 WORD DLTA against
  /// the cleared black bitmap permitted by the ANIM8 specification.
  /// </summary>
  public static byte[] ToBytes(IffAnim8File file) {
    if (file.PixelData is not { Length: > 0 }) {
      if (file.RawData is not { Length: >= IffAnim8File.MinFileSize })
        throw new InvalidDataException("ANIM8 file has neither writable RGB24 pixels nor valid raw data.");

      return file.RawData[..];
    }

    var width = file.Width;
    var height = file.Height;
    if (width is <= 0 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file), $"ANIM8 width must be between 1 and {ushort.MaxValue} pixels.");
    if (height is <= 0 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file), $"ANIM8 height must be between 1 and {ushort.MaxValue} pixels.");

    var expectedLength = (long)width * height * 3;
    if (expectedLength > int.MaxValue || file.PixelData.LongLength != expectedLength)
      throw new InvalidDataException($"ANIM8 RGB24 data length does not match {width}x{height} pixels.");

    var indexed = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData,
    }.EnsureIndexedAtMost(256);

    byte[] palette = indexed.Palette is { Length: > 0 } p ? p : [0, 0, 0];
    var paletteCount = Math.Clamp(indexed.PaletteCount > 0 ? indexed.PaletteCount : palette.Length / 3, 1, 256);
    var numPlanes = 1;
    while ((1 << numPlanes) < paletteCount)
      ++numPlanes;

    var planar = PlanarConverter.ChunkyToIlbmPlanar(indexed.PixelData, width, height, numPlanes);
    var rowBytes = ((width + 15) / 16) * 2;
    var dlta = _EncodeShortVerticalDelta(planar, height, numPlanes, rowBytes);
    var firstFrame = _BuildFirstFrame(width, height, numPlanes, palette, dlta);

    var formDataSize = checked(4 + firstFrame.Length);
    using var result = new MemoryStream(checked(8 + formDataSize));
    _WriteChunkHeader(result, "FORM"u8, formDataSize);
    result.Write("ANIM"u8);
    result.Write(firstFrame);
    return result.ToArray();
  }

  private static byte[] _BuildFirstFrame(int width, int height, int numPlanes, byte[] palette, byte[] dlta) {
    using var chunks = new MemoryStream();

    Span<byte> bmhd = stackalloc byte[20];
    bmhd.Clear();
    BinaryPrimitives.WriteUInt16BigEndian(bmhd, (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd[2..], (ushort)height);
    bmhd[8] = (byte)numPlanes;
    bmhd[14] = 1;
    bmhd[15] = 1;
    BinaryPrimitives.WriteInt16BigEndian(bmhd[16..], unchecked((short)width));
    BinaryPrimitives.WriteInt16BigEndian(bmhd[18..], unchecked((short)height));
    _WriteChunk(chunks, "BMHD"u8, bmhd);

    Span<byte> anhd = stackalloc byte[_AnhdSize];
    anhd.Clear();
    anhd[0] = 8;
    BinaryPrimitives.WriteUInt32BigEndian(anhd[14..], 1);
    // ANHD bits bit 0 remains clear: method 8 therefore carries WORD rather than LONG data.
    _WriteChunk(chunks, "ANHD"u8, anhd);

    _WriteChunk(chunks, "CMAP"u8, palette);
    _WriteChunk(chunks, "DLTA"u8, dlta);

    var chunkData = chunks.ToArray();
    var formDataSize = checked(4 + chunkData.Length);
    using var frame = new MemoryStream(checked(8 + formDataSize));
    _WriteChunkHeader(frame, "FORM"u8, formDataSize);
    frame.Write("ILBM"u8);
    frame.Write(chunkData);
    return frame.ToArray();
  }

  private static byte[] _EncodeShortVerticalDelta(ReadOnlySpan<byte> planar, int height, int numPlanes, int rowBytes) {
    using var payload = new MemoryStream();
    payload.Write(new byte[_DltaPointerTableSize]);

    Span<uint> planePointers = stackalloc uint[_DltaPlanePointerCount];
    planePointers.Clear();
    var columns = rowBytes / sizeof(ushort);

    for (var plane = 0; plane < numPlanes; ++plane) {
      using var planeData = new MemoryStream();
      var planeChanged = false;

      for (var column = 0; column < columns; ++column) {
        if (!_ColumnHasChange(planar, height, numPlanes, rowBytes, plane, column)) {
          _WriteUInt16BigEndian(planeData, 0);
          continue;
        }

        planeChanged = true;
        _WriteColumn(planar, height, numPlanes, rowBytes, plane, column, planeData);
      }

      if (!planeChanged)
        continue;

      planePointers[plane] = checked((uint)payload.Position);
      planeData.Position = 0;
      planeData.CopyTo(payload);
    }

    var result = payload.ToArray();
    for (var plane = 0; plane < _DltaPlanePointerCount; ++plane)
      BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(plane * sizeof(uint), sizeof(uint)), planePointers[plane]);

    return result;
  }

  private static bool _ColumnHasChange(ReadOnlySpan<byte> planar, int height, int numPlanes, int rowBytes, int plane, int column) {
    for (var row = 0; row < height; ++row)
      if (_ReadWord(planar, row, numPlanes, rowBytes, plane, column) != 0)
        return true;

    return false;
  }

  private static void _WriteColumn(ReadOnlySpan<byte> planar, int height, int numPlanes, int rowBytes, int plane, int column, Stream destination) {
    using var operations = new MemoryStream();
    var operationCount = 0;
    var row = 0;

    while (row < height) {
      var value = _ReadWord(planar, row, numPlanes, rowBytes, plane, column);
      if (value == 0) {
        var count = 1;
        while (count < _MaximumUniqueOrSkipCount
               && row + count < height
               && _ReadWord(planar, row + count, numPlanes, rowBytes, plane, column) == 0)
          ++count;

        _WriteUInt16BigEndian(operations, (ushort)count);
        ++operationCount;
        row += count;
        continue;
      }

      var sameCount = 1;
      while (sameCount < ushort.MaxValue
             && row + sameCount < height
             && _ReadWord(planar, row + sameCount, numPlanes, rowBytes, plane, column) == value)
        ++sameCount;

      if (sameCount >= 2) {
        _WriteUInt16BigEndian(operations, 0);
        _WriteUInt16BigEndian(operations, (ushort)sameCount);
        _WriteUInt16BigEndian(operations, value);
        ++operationCount;
        row += sameCount;
        continue;
      }

      var start = row++;
      while (row - start < _MaximumUniqueOrSkipCount && row < height) {
        var next = _ReadWord(planar, row, numPlanes, rowBytes, plane, column);
        if (next == 0)
          break;

        if (row + 1 < height && _ReadWord(planar, row + 1, numPlanes, rowBytes, plane, column) == next)
          break;

        ++row;
      }

      var uniqueCount = row - start;
      _WriteUInt16BigEndian(operations, (ushort)(0x8000 | uniqueCount));
      for (var i = 0; i < uniqueCount; ++i)
        _WriteUInt16BigEndian(operations, _ReadWord(planar, start + i, numPlanes, rowBytes, plane, column));
      ++operationCount;
    }

    _WriteUInt16BigEndian(destination, checked((ushort)operationCount));
    operations.Position = 0;
    operations.CopyTo(destination);
  }

  private static ushort _ReadWord(ReadOnlySpan<byte> planar, int row, int numPlanes, int rowBytes, int plane, int column) {
    var offset = checked((row * numPlanes + plane) * rowBytes + column * sizeof(ushort));
    return BinaryPrimitives.ReadUInt16BigEndian(planar[offset..]);
  }

  private static void _WriteChunk(Stream stream, ReadOnlySpan<byte> id, ReadOnlySpan<byte> payload) {
    _WriteChunkHeader(stream, id, payload.Length);
    stream.Write(payload);
    if ((payload.Length & 1) != 0)
      stream.WriteByte(0);
  }

  private static void _WriteChunkHeader(Stream stream, ReadOnlySpan<byte> id, int size) {
    if (id.Length != 4)
      throw new ArgumentException("IFF chunk identifiers are exactly four bytes.", nameof(id));

    Span<byte> header = stackalloc byte[8];
    id.CopyTo(header);
    BinaryPrimitives.WriteInt32BigEndian(header[4..], size);
    stream.Write(header);
  }

  private static void _WriteUInt16BigEndian(Stream stream, ushort value) {
    Span<byte> buffer = stackalloc byte[sizeof(ushort)];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
