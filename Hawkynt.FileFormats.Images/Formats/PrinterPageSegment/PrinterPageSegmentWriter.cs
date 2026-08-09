using System;
using System.IO;

namespace FileFormat.PrinterPageSegment;

/// <summary>Assembles an IBM printer page segment out of structured fields.</summary>
/// <remarks>
/// A segment may be a mosaic of cells and this writes it as one: a descriptor stating the size and
/// the cell width, one cell position at the origin asking for no fill, the raster, and the
/// end-of-image marker. Splitting a picture into several CELLS is what a printer driver does to fit
/// its own buffers and buys a reader nothing.
/// <para/>
/// The raster is split across several picture-data FIELDS, which is a different thing and is forced:
/// a field states its length in sixteen bits, so one of them holds a little under 64 kilobytes and a
/// page of A4 at 300 dots does not fit in that. Rows carry on where the last field stopped — the
/// reader keeps its row counter across fields — so the split is at a row boundary and nowhere else.
/// <para/>
/// Two numbers in the descriptor are easy to put in the wrong place and were confirmed by handing
/// files over one at a time: the size is at 18 and 20, and the cell width is at 28 rather than 24 — a
/// decoy value at 24 changed nothing about what came back. The end-of-image field is not decoration
/// either. A segment whose data stops exactly at the bottom row with nothing behind it is refused,
/// so the marker is what makes the last row readable.
/// </remarks>
public static class PrinterPageSegmentWriter {

  /// <summary>Image input descriptor: the size of the whole picture and the width of a cell.</summary>
  private const int _IMAGE_INPUT_DESCRIPTOR = 0xD3A67B;

  /// <summary>Image cell position: where the next piece of the picture goes.</summary>
  private const int _IMAGE_CELL_POSITION = 0xD3AC7B;

  /// <summary>Image picture data: the bits themselves.</summary>
  private const int _IMAGE_PICTURE_DATA = 0xD3EE7B;

  /// <summary>End of image, which is how a segment says it is finished.</summary>
  private const int _END_OF_IMAGE = 0xD3A97B;

  /// <summary>Bytes of introduction ahead of every field's content.</summary>
  private const int _INTRODUCTION = 9;

  /// <summary>How much of the stated length the introduction itself accounts for.</summary>
  private const int _LENGTH_COVERS = 8;

  /// <summary>Bytes of descriptor the reader wants.</summary>
  private const int _DESCRIPTOR_SIZE = 36;

  /// <summary>Bytes of cell position the reader wants.</summary>
  private const int _CELL_POSITION_SIZE = 12;

  /// <summary>The value a fill dimension carries when there is no rectangle to fill.</summary>
  private const int _NO_FILL = 0xFFFF;

  /// <summary>Most content one field may carry, its length being sixteen bits including the eight.</summary>
  private const int _MAX_CONTENT = 0xFFFF - _LENGTH_COVERS;

  public static byte[] ToBytes(PrinterPageSegmentFile file) {
    var width = file.Width;
    var height = file.Height;
    var stride = file.Stride;
    if ((width & 7) != 0)
      throw new InvalidDataException($"An image is a whole number of bytes wide; {width} pels is not.");
    if (stride > _MAX_CONTENT)
      throw new InvalidDataException(
        $"A field holds {_MAX_CONTENT} bytes and one row of {width} pels takes {stride}, so no field can carry a row.");

    var raster = new byte[(long)stride * height];
    var pixels = file.PixelData ?? [];
    pixels.AsSpan(0, Math.Min(pixels.Length, raster.Length)).CopyTo(raster);

    using var output = new MemoryStream();

    var descriptor = new byte[_DESCRIPTOR_SIZE];
    _WriteBigEndian16(descriptor, 18, width);
    _WriteBigEndian16(descriptor, 20, height);
    _WriteBigEndian16(descriptor, 28, width);
    _Field(output, _IMAGE_INPUT_DESCRIPTOR, descriptor);

    // One cell, at the origin, the full width of the picture, and no rectangle to clear behind it.
    var position = new byte[_CELL_POSITION_SIZE];
    _WriteBigEndian16(position, 4, width);
    _WriteBigEndian16(position, 8, _NO_FILL);
    _WriteBigEndian16(position, 10, _NO_FILL);
    _Field(output, _IMAGE_CELL_POSITION, position);

    var perField = Math.Max(1, _MAX_CONTENT / stride) * stride;
    for (var at = 0; at < raster.Length; at += perField)
      _Field(output, _IMAGE_PICTURE_DATA, raster.AsSpan(at, Math.Min(perField, raster.Length - at)));

    _Field(output, _END_OF_IMAGE, []);

    return output.ToArray();
  }

  private static void _Field(Stream output, int type, ReadOnlySpan<byte> content) {
    Span<byte> introduction = stackalloc byte[_INTRODUCTION];
    introduction[0] = PrinterPageSegmentReader.FieldIntroducer;
    var length = _LENGTH_COVERS + content.Length;
    introduction[1] = (byte)(length >> 8);
    introduction[2] = (byte)length;
    introduction[3] = (byte)(type >> 16);
    introduction[4] = (byte)(type >> 8);
    introduction[5] = (byte)type;

    output.Write(introduction);
    output.Write(content);
  }

  private static void _WriteBigEndian16(byte[] target, int at, int value) {
    target[at] = (byte)(value >> 8);
    target[at + 1] = (byte)value;
  }
}
