using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Mrf;

/// <summary>Reads Monochrome Recursive Format pictures from bytes, streams, or file paths.</summary>
public static class MrfReader {

  /// <summary>
  /// Bigger than anything <c>zgv</c> was ever pointed at, and it keeps a file whose four bytes
  /// happen to read <c>MRF1</c> from asking for a canvas of gigabytes before it fails.
  /// </summary>
  private const int _MaxDimension = 1 << 16;

  public static MrfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MRF picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MrfFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static MrfFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= MrfFile.HeaderSize || !data[..4].SequenceEqual(MrfFile.Magic))
      throw new InvalidDataException("Not an MRF picture: it does not open with MRF1.");

    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]);

    if (width is < 1 or > _MaxDimension || height is < 1 or > _MaxDimension)
      throw new InvalidDataException($"An MRF picture states a size of {width} by {height}.");

    // The colour sibling PRF1 reads this byte as a depth and a plane count and shares everything
    // else, so anything but nought is a file this does not decode rather than one that is damaged.
    if (data[12] != 0)
      throw new InvalidDataException($"An MRF picture reserves byte twelve and this one holds {data[12]}.");

    // Squares are coded over a canvas rounded up to whole tiles, and the picture is the top-left
    // corner of it. Decoding at the stated size instead would put every row after the first at the
    // wrong offset.
    var tilesAcross = (width + MrfFile.TileSize - 1) / MrfFile.TileSize;
    var tilesDown = (height + MrfFile.TileSize - 1) / MrfFile.TileSize;
    var paddedWidth = tilesAcross * MrfFile.TileSize;

    var canvas = new byte[paddedWidth * tilesDown * MrfFile.TileSize];
    var reader = new _BitReader(data[MrfFile.HeaderSize..]);

    for (var tileY = 0; tileY < tilesDown; ++tileY)
      for (var tileX = 0; tileX < tilesAcross; ++tileX)
        _ReadSquare(ref reader, canvas, paddedWidth, tileX * MrfFile.TileSize, tileY * MrfFile.TileSize, MrfFile.TileSize);

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
      canvas.AsSpan(y * paddedWidth, width).CopyTo(pixels.AsSpan(y * width));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }

  public static MrfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Decodes one square, splitting it into quarters until each is one colour throughout.</summary>
  private static void _ReadSquare(ref _BitReader reader, byte[] canvas, int stride, int left, int top, int size) {
    // A single pixel cannot be split, so no bit is spent saying it is uniform — only its colour.
    if (size == 1 || reader.ReadBit() == 1) {
      var colour = (byte)reader.ReadBit();
      for (var y = 0; y < size; ++y)
        canvas.AsSpan((top + y) * stride + left, size).Fill(colour);

      return;
    }

    var half = size >> 1;
    _ReadSquare(ref reader, canvas, stride, left, top, half);
    _ReadSquare(ref reader, canvas, stride, left + half, top, half);
    _ReadSquare(ref reader, canvas, stride, left, top + half, half);
    _ReadSquare(ref reader, canvas, stride, left + half, top + half, half);
  }

  /// <summary>Hands out the bit stream one bit at a time, most significant first.</summary>
  private ref struct _BitReader {

    private readonly ReadOnlySpan<byte> _data;
    private int _at;
    private int _bit;

    public _BitReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._at = 0;
      this._bit = 7;
    }

    public int ReadBit() {
      if (this._at >= this._data.Length)
        throw new InvalidDataException("An MRF picture ends in the middle of its bit stream.");

      var value = (this._data[this._at] >> this._bit) & 1;
      if (--this._bit < 0) {
        this._bit = 7;
        ++this._at;
      }

      return value;
    }
  }
}
