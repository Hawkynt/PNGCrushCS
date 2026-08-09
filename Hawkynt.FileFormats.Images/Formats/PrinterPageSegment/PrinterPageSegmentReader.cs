using System;
using System.IO;

namespace FileFormat.PrinterPageSegment;

/// <summary>Walks the structured fields of an IBM printer page segment and lays out its cells.</summary>
/// <remarks>
/// The walk happens twice, which is how the original does it and why the reader is shaped this way.
/// The first pass runs until the picture starts, so that the descriptor's size is known before there
/// is anywhere to put pixels; the second begins again at that field and does the drawing. A file
/// whose first cell arrives before any descriptor has no size to be drawn at and is refused.
/// <para/>
/// The two passes accept different sets of fields, and that is not an oversight. The first walks past
/// the housekeeping fields — the begin and end markers, the map and the descriptor — and stops at the
/// first cell; the second knows only cells, picture data and the end-of-image marker, and stops dead
/// at anything else. So a field that is merely ignored on the way in is fatal on the way through.
/// </remarks>
public static class PrinterPageSegmentReader {

  /// <summary>The byte every structured field opens with.</summary>
  public const byte FieldIntroducer = 0x5A;

  /// <summary>Bytes of introduction: the introducer, a length, a three-byte type, a flag and a sequence.</summary>
  public const int IntroductionSize = 9;

  /// <summary>How much of the length the introduction itself accounts for.</summary>
  private const int _LENGTH_COVERS = 8;

  /// <summary>Image input descriptor: the size of the whole picture and the width of a cell.</summary>
  private const int _IMAGE_INPUT_DESCRIPTOR = 0xD3A67B;

  /// <summary>Image cell position: where the next piece of the picture goes.</summary>
  private const int _IMAGE_CELL_POSITION = 0xD3AC7B;

  /// <summary>Image picture data: the bits themselves.</summary>
  private const int _IMAGE_PICTURE_DATA = 0xD3EE7B;

  /// <summary>End of image, which is how a segment says it is finished.</summary>
  private const int _END_OF_IMAGE = 0xD3A97B;

  /// <summary>Bytes of the descriptor that are read.</summary>
  private const int _DESCRIPTOR_SIZE = 36;

  /// <summary>Bytes of a cell position that are read.</summary>
  private const int _CELL_POSITION_SIZE = 12;

  /// <summary>The value a fill dimension carries when there is no rectangle to fill.</summary>
  private const int _NO_FILL = 0xFFFF;

  /// <summary>Largest picture the original will allocate, either way round.</summary>
  private const int _MAX_EXTENT = PrinterPageSegmentFile.MaximumExtent;

  public static PrinterPageSegmentFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Page segment not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrinterPageSegmentFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static PrinterPageSegmentFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Whether a type is one the walk to the picture accepts.</summary>
  internal static bool IsFirstPassType(int type) => type switch {
    _IMAGE_INPUT_DESCRIPTOR or _IMAGE_CELL_POSITION or _IMAGE_PICTURE_DATA => true,
    0xD3EEEE or 0xD3A85F or 0xD3A87B or 0xD3A77B => true,
    _ => false,
  };

  public static PrinterPageSegmentFile FromSpan(ReadOnlySpan<byte> data) {
    var at = 0;
    var width = 0;
    var height = 0;
    var cellWidth = 0;

    // First pass: read the descriptor and walk past the housekeeping, stopping where the picture
    // begins without consuming that field's introduction.
    while (true) {
      var (length, type) = _Introduction(data, at);
      if (type is _IMAGE_CELL_POSITION or _IMAGE_PICTURE_DATA)
        break;

      var payload = length - _LENGTH_COVERS;
      var body = at + IntroductionSize;
      if (body + payload > data.Length)
        throw new InvalidDataException("A structured field states more content than the file holds.");

      switch (type) {
        case _IMAGE_INPUT_DESCRIPTOR:
          if (payload < _DESCRIPTOR_SIZE)
            throw new InvalidDataException(
              $"An image input descriptor is read for {_DESCRIPTOR_SIZE} bytes; this one states {payload}.");

          (width, height, cellWidth) = _Descriptor(data.Slice(body, _DESCRIPTOR_SIZE));
          break;
        case 0xD3EEEE:
        case 0xD3A85F:
        case 0xD3A87B:
        case 0xD3A77B:
          break;
        default:
          throw new InvalidDataException(
            $"A page segment carries no field of type {type:X6}; an IOCA one carries several, and is not this.");
      }

      at = body + payload;
    }

    if (width is <= 0 or > _MAX_EXTENT || height is <= 0 or > _MAX_EXTENT)
      throw new InvalidDataException(
        "The picture starts before anything states its size, so there is nowhere to put it.");

    var stride = (width + 7) / 8;
    var pixels = new byte[stride * height];

    // Second pass, from the field the first one stopped at. The raster is already paper; the fields
    // put ink on it.
    var xOffset = 0;
    var yOffset = 0;
    while (true) {
      var (length, type) = _Introduction(data, at);
      var payload = length - _LENGTH_COVERS;
      var body = at + IntroductionSize;
      if (type == _END_OF_IMAGE)
        break;

      if (body + payload > data.Length)
        throw new InvalidDataException("A structured field states more content than the file holds.");

      switch (type) {
        case _IMAGE_CELL_POSITION:
          if (payload < _CELL_POSITION_SIZE)
            throw new InvalidDataException(
              $"A cell position is read for {_CELL_POSITION_SIZE} bytes; this one states {payload}.");

          (xOffset, yOffset, cellWidth) = _CellPosition(
            data.Slice(body, _CELL_POSITION_SIZE), pixels, width, height, stride);
          break;
        case _IMAGE_PICTURE_DATA:
          if (_PictureData(data.Slice(body, payload), pixels, height, stride, xOffset, ref yOffset, cellWidth))
            return new() { Width = width, Height = height, PixelData = pixels };

          break;
        default:
          throw new InvalidDataException(
            $"Once the picture has started only cells and their data may follow; {type:X6} may not.");
      }

      at = body + payload;
    }

    return new() { Width = width, Height = height, PixelData = pixels };
  }

  /// <summary>Reads one field's introduction without consuming it.</summary>
  private static (int Length, int Type) _Introduction(ReadOnlySpan<byte> data, int at) {
    if (at + IntroductionSize > data.Length)
      throw new InvalidDataException("The file ends part-way through a structured field.");
    if (data[at] != FieldIntroducer)
      throw new InvalidDataException(
        $"Every structured field opens with {FieldIntroducer:X2}; this one opens with {data[at]:X2}.");

    var length = (data[at + 1] << 8) | data[at + 2];
    if (length < _LENGTH_COVERS)
      throw new InvalidDataException($"A field's length covers at least {_LENGTH_COVERS} bytes; this one states {length}.");

    return (length, (data[at + 3] << 16) | (data[at + 4] << 8) | data[at + 5]);
  }

  /// <summary>
  /// Reads the size of the picture and the width of a cell out of the image input descriptor.
  /// </summary>
  /// <remarks>
  /// Both widths have to divide by eight, which is what makes a row a whole number of bytes and is
  /// checked rather than assumed — the original refuses a file that breaks it, and so does this.
  /// A cell width of zero with nothing behind it means the cell is the whole picture wide.
  /// </remarks>
  private static (int Width, int Height, int CellWidth) _Descriptor(ReadOnlySpan<byte> descriptor) {
    var width = (descriptor[18] << 8) | descriptor[19];
    var height = (descriptor[20] << 8) | descriptor[21];
    if ((width & 7) != 0)
      throw new InvalidDataException($"An image is a whole number of bytes wide; {width} pels is not.");

    var stated = (descriptor[28] << 8) | descriptor[29];
    var behind = (descriptor[30] << 8) | descriptor[31];
    var cellWidth = stated == 0 && behind == 0 ? width : stated;
    if ((cellWidth & 7) != 0)
      throw new InvalidDataException($"A cell is a whole number of bytes wide; {cellWidth} pels is not.");

    return (width, height, cellWidth);
  }

  /// <summary>
  /// Places the next cell, and clears the rectangle the field asks for when it asks for one.
  /// </summary>
  /// <remarks>
  /// The cell height at bytes six and seven is read by nobody, here or there. The fill runs only when
  /// neither of its dimensions is the "no rectangle" value, and it stops one row short of where its
  /// stated height would reach — that off-by-one is the original's and is kept, because a reader that
  /// cleared the extra row would rub out ink the tool leaves standing.
  /// </remarks>
  private static (int X, int Y, int CellWidth) _CellPosition(
    ReadOnlySpan<byte> position, byte[] pixels, int width, int height, int stride) {
    var x = (position[0] << 8) | position[1];
    var y = (position[2] << 8) | position[3];
    var cellWidth = (position[4] << 8) | position[5];
    if ((x & 7) != 0)
      throw new InvalidDataException($"A cell starts on a byte boundary; {x} pels is not one.");
    if ((cellWidth & 7) != 0)
      throw new InvalidDataException($"A cell is a whole number of bytes wide; {cellWidth} pels is not.");

    var fillWidth = (position[8] << 8) | position[9];
    var fillHeight = (position[10] << 8) | position[11];
    if (fillWidth == _NO_FILL || fillHeight == _NO_FILL)
      return (x, y, cellWidth);

    if (height <= y + fillHeight)
      fillHeight = height - y - 1;

    var span = Math.Min(fillWidth, (width + 7) & ~7);
    var bytes = (span + 7) >> 3;
    var to = y * stride + (x >> 3);
    for (var row = 0; row < fillHeight; ++row, to += stride) {
      if (to < 0 || to >= pixels.Length)
        break;

      Array.Clear(pixels, to, Math.Min(bytes, pixels.Length - to));
    }

    return (x, y, cellWidth);
  }

  /// <summary>
  /// Copies one field's worth of rows into the raster, and says whether that finished the picture.
  /// </summary>
  /// <remarks>
  /// There is no coding here of any kind: a row is <c>cell width / 8</c> bytes taken as they lie,
  /// most significant bit leftmost, and the next row lands one stride further down. The cell width is
  /// the row length of the data and has nothing to do with the width of the picture, which is what
  /// lets one segment carry several columns of cells.
  /// <para/>
  /// Two things end the whole read rather than just this field: reaching the last row with data still
  /// in hand, and a field arriving when the last row is already behind. Both are how the original
  /// finishes a file that carries no end-of-image marker, and both count as success.
  /// <para/>
  /// Running out of data exactly at the last row is NOT one of them, and the difference is real: a
  /// segment holding exactly as many rows as the picture is tall and no end-of-image marker after
  /// them is refused, while the same segment with one byte more of data is read. That was settled by
  /// building both and handing them over.
  /// <para/>
  /// Bytes past the last whole row are dropped, so the coding does not run on across a field boundary
  /// even though the rows do — the next field starts a new row at the row this one stopped at.
  /// </remarks>
  private static bool _PictureData(
    ReadOnlySpan<byte> content, byte[] pixels, int height, int stride, int x, ref int y, int cellWidth) {
    var rowBytes = cellWidth >> 3;
    if (rowBytes <= 0)
      throw new InvalidDataException("A cell no bytes wide carries no rows, and the picture cannot be finished.");

    if (content.Length < rowBytes)
      return false;
    if (height <= y)
      return true;

    var at = 0;
    var to = y * stride + (x >> 3);
    var row = y;
    for (; content.Length - at >= rowBytes; at += rowBytes, to += stride, ++row) {
      if (row == height) {
        y = row;
        return true;
      }

      // The original writes the cell's bytes with no regard for where the row ends and will run into
      // the rows below; clamping is the one place this reader knowingly parts company with it.
      if (to >= pixels.Length)
        break;

      content.Slice(at, Math.Min(rowBytes, pixels.Length - to)).CopyTo(pixels.AsSpan(to));
    }

    y = row;
    return false;
  }
}
