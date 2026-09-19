using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Fpx;

namespace FileFormat.Excel;

/// <summary>Reads and writes the legacy BIFF8/OLE form of an Excel workbook picture.</summary>
/// <remarks>
/// [MS-XLS] defines BkHim (record 0x00E9) as the worksheet background image. That is the smallest
/// native BIFF8 representation of the image contract used by <see cref="ExcelFile"/>: unlike an
/// OfficeArt drawing it needs neither a drawing-group hierarchy nor an OBJ record, and the picture
/// is still a first-class Excel worksheet background rather than an unrelated OLE payload.
/// </remarks>
internal static class ExcelBinaryFile {

  private const ushort _Bof = 0x0809;
  private const ushort _Eof = 0x000A;
  private const ushort _Continue = 0x003C;
  private const ushort _CodePage = 0x0042;
  private const ushort _Window1 = 0x003D;
  private const ushort _Font = 0x0031;
  private const ushort _Xf = 0x00E0;
  private const ushort _Style = 0x0293;
  private const ushort _BoundSheet8 = 0x0085;
  private const ushort _DateMode = 0x0022;
  private const ushort _Index = 0x020B;
  private const ushort _DefColWidth = 0x0055;
  private const ushort _Dimensions = 0x0200;
  private const ushort _BkHim = 0x00E9;
  private const ushort _Window2 = 0x023E;

  private const int _MaximumRecordPayload = 8224;
  private const short _BitmapClipboardFormat = 0x0009;
  private const short _BkHimReserved = 0x0001;

  // Excel.Sheet.8. This is advisory CFB metadata; BIFF content is authoritative.
  private static readonly Guid _Excel8ClassId = new("00020820-0000-0000-C000-000000000046");

  internal static ExcelFile Read(ReadOnlySpan<byte> data) {
    var compound = new CompoundFile(data);
    byte[]? workbook = null;
    foreach (var pair in compound.Streams()) {
      if (pair.Value.Type != CompoundFile.EntryStream)
        continue;
      if (!pair.Key.Equals("/Workbook", StringComparison.OrdinalIgnoreCase)
          && !pair.Key.Equals("/Book", StringComparison.OrdinalIgnoreCase))
        continue;

      workbook = compound.Read(pair.Value);
      break;
    }

    if (workbook is null)
      throw new InvalidDataException("Excel compound file does not contain a Workbook or Book stream.");

    var image = _ReadBackground(workbook).EnsureFormat(PixelFormat.Rgb24);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Images = [image],
      Kind = ExcelOpenXmlKind.LegacyWorkbook,
    };
  }

  internal static byte[] Write(RawImage source) {
    var image = source.EnsureFormat(PixelFormat.Rgb24);
    var worksheet = _BuildWorksheet(image);
    var globals = _BuildWorkbookGlobals(worksheet.Bytes.Length, out var boundSheetPointerOffset);

    var sheetOffset = globals.Length;
    BinaryPrimitives.WriteUInt32LittleEndian(globals.AsSpan(boundSheetPointerOffset), checked((uint)sheetOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(
      worksheet.Bytes.AsSpan(worksheet.IndexIbXfPatch),
      checked((uint)(sheetOffset + worksheet.DefColWidthOffset)));

    var workbook = new byte[checked(globals.Length + worksheet.Bytes.Length)];
    globals.CopyTo(workbook, 0);
    worksheet.Bytes.CopyTo(workbook, globals.Length);

    var compound = new CompoundFileWriter(_Excel8ClassId);
    compound.AddStream(0, "Workbook", workbook);
    return compound.Build();
  }

  private static WorksheetBytes _BuildWorksheet(RawImage image) {
    using var body = new MemoryStream();
    _WriteBof(body, 0x0010);

    var indexRecord = checked((int)body.Position);
    Span<byte> index = stackalloc byte[16];
    index.Clear();
    _WriteRecord(body, _Index, index);
    var indexIbXfPatch = checked(indexRecord + 4 + 12);

    // A background does not populate cells, so the used-cell bounds are empty.
    Span<byte> dimensions = stackalloc byte[14];
    dimensions.Clear();
    _WriteRecord(body, _Dimensions, dimensions);

    var defColWidthOffset = checked((int)body.Position);
    Span<byte> columnWidth = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(columnWidth, 8);
    _WriteRecord(body, _DefColWidth, columnWidth);

    _WriteBackground(body, _BuildBitmapCore(image));

    Span<byte> window2 = stackalloc byte[18];
    window2.Clear();
    BinaryPrimitives.WriteUInt16LittleEndian(window2, 0x06B6); // visible, grid, headings, selected, normal view
    BinaryPrimitives.WriteUInt16LittleEndian(window2[6..], 0x0040); // automatic grid colour
    BinaryPrimitives.WriteUInt16LittleEndian(window2[10..], 100);  // normal zoom
    _WriteRecord(body, _Window2, window2);
    _WriteRecord(body, _Eof, ReadOnlySpan<byte>.Empty);

    return new(body.ToArray(), indexIbXfPatch, defColWidthOffset);
  }

  private static byte[] _BuildWorkbookGlobals(int worksheetLength, out int boundSheetPointerOffset) {
    using var stream = new MemoryStream();
    _WriteBof(stream, 0x0005);

    Span<byte> codePage = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(codePage, 1252);
    _WriteRecord(stream, _CodePage, codePage);

    Span<byte> window1 = stackalloc byte[18];
    window1.Clear();
    BinaryPrimitives.WriteUInt16LittleEndian(window1[4..], 0x3FCF);
    BinaryPrimitives.WriteUInt16LittleEndian(window1[6..], 0x20E4);
    BinaryPrimitives.WriteUInt16LittleEndian(window1[8..], 0x0038); // scroll bars + sheet tabs
    BinaryPrimitives.WriteUInt16LittleEndian(window1[14..], 1);     // one selected tab
    BinaryPrimitives.WriteUInt16LittleEndian(window1[16..], 600);   // 60% tab-bar share
    _WriteRecord(stream, _Window1, window1);

    Span<byte> dateMode = stackalloc byte[2];
    dateMode.Clear();
    _WriteRecord(stream, _DateMode, dateMode);

    // BIFF8 consumers expect the built-in font/XF/style collections even when the sheet has no cells.
    for (var i = 0; i < 5; ++i)
      _WriteRecord(stream, _Font, _DefaultFont());
    for (var i = 0; i < 16; ++i)
      _WriteRecord(stream, _Xf, _DefaultXf(style: true));
    _WriteRecord(stream, _Xf, _DefaultXf(style: false));

    Span<byte> normalStyle = stackalloc byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(normalStyle, 0x8000); // XF 0, built-in
    normalStyle[2] = 0x00; // Normal
    normalStyle[3] = 0xFF;
    _WriteRecord(stream, _Style, normalStyle);

    Span<byte> boundSheet = stackalloc byte[14];
    boundSheet.Clear();
    boundSheet[6] = 6;
    boundSheet[7] = 0; // compressed 8-bit characters
    "Sheet1"u8.CopyTo(boundSheet[8..]);
    var recordStart = checked((int)stream.Position);
    _WriteRecord(stream, _BoundSheet8, boundSheet);
    boundSheetPointerOffset = checked(recordStart + 4);

    _WriteRecord(stream, _Eof, ReadOnlySpan<byte>.Empty);
    _ = checked(stream.Length + worksheetLength);
    return stream.ToArray();
  }

  private static byte[] _DefaultFont() {
    var result = new byte[21];
    BinaryPrimitives.WriteUInt16LittleEndian(result, 200);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 0x7FFF);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 400);
    result[12] = 0;
    result[13] = 1;
    result[14] = 5;
    result[15] = 0;
    "Arial"u8.CopyTo(result.AsSpan(16));
    return result;
  }

  private static byte[] _DefaultXf(bool style) {
    var result = new byte[20];
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), style ? (ushort)0xFFF5 : (ushort)0x0001);
    result[6] = 0x20;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(18), 0x20C0);
    return result;
  }

  private static void _WriteBackground(Stream stream, byte[] dib) {
    var total = checked(8 + dib.Length);
    var firstPayloadLength = Math.Min(total, _MaximumRecordPayload);
    var first = new byte[firstPayloadLength];
    BinaryPrimitives.WriteInt16LittleEndian(first, _BitmapClipboardFormat);
    BinaryPrimitives.WriteInt16LittleEndian(first.AsSpan(2), _BkHimReserved);
    BinaryPrimitives.WriteInt32LittleEndian(first.AsSpan(4), dib.Length);
    var firstDibBytes = firstPayloadLength - 8;
    dib.AsSpan(0, firstDibBytes).CopyTo(first.AsSpan(8));
    _WriteRecord(stream, _BkHim, first);

    for (var at = firstDibBytes; at < dib.Length;) {
      var count = Math.Min(_MaximumRecordPayload, dib.Length - at);
      _WriteRecord(stream, _Continue, dib.AsSpan(at, count));
      at += count;
    }
  }

  private static byte[] _BuildBitmapCore(RawImage image) {
    if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image), "BIFF8 BkHim BITMAPCOREHEADER is limited to 65535x65535 pixels.");

    var rowBytes = checked(image.Width * 3);
    var stride = checked((rowBytes + 3) & ~3);
    var result = new byte[checked(12 + stride * image.Height)];
    BinaryPrimitives.WriteUInt32LittleEndian(result, 12);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)image.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), checked((ushort)image.Height));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), 24);

    for (var y = 0; y < image.Height; ++y) {
      var source = image.PixelData.AsSpan(checked(y * rowBytes), rowBytes);
      var destinationY = image.Height - 1 - y;
      source.CopyTo(result.AsSpan(checked(12 + destinationY * stride), rowBytes));
    }

    return result;
  }

  private static RawImage _ReadBackground(ReadOnlySpan<byte> workbook) {
    for (var at = 0; at + 4 <= workbook.Length;) {
      var type = BinaryPrimitives.ReadUInt16LittleEndian(workbook[at..]);
      var length = BinaryPrimitives.ReadUInt16LittleEndian(workbook[(at + 2)..]);
      var payloadStart = checked(at + 4);
      var payloadEnd = checked(payloadStart + length);
      if (payloadEnd > workbook.Length)
        throw new InvalidDataException($"BIFF record 0x{type:X4} extends past the end of the Workbook stream.");

      if (type == _BkHim)
        return _ReadBkHim(workbook, payloadStart, length);

      at = payloadEnd;
    }

    throw new InvalidDataException("Excel Workbook stream contains no BkHim worksheet background image.");
  }

  private static RawImage _ReadBkHim(ReadOnlySpan<byte> workbook, int payloadStart, int length) {
    if (length < 8)
      throw new InvalidDataException("BkHim record is shorter than its 8-byte header.");

    var first = workbook.Slice(payloadStart, length);
    var format = BinaryPrimitives.ReadInt16LittleEndian(first);
    var reserved = BinaryPrimitives.ReadInt16LittleEndian(first[2..]);
    var blobLength = BinaryPrimitives.ReadInt32LittleEndian(first[4..]);
    if (format != _BitmapClipboardFormat || reserved != _BkHimReserved || blobLength <= 0)
      throw new InvalidDataException("BkHim image is not a supported BIFF8 bitmap background.");

    var dib = new byte[blobLength];
    var copied = Math.Min(blobLength, length - 8);
    first.Slice(8, copied).CopyTo(dib);
    var next = checked(payloadStart + length);

    while (copied < blobLength) {
      if (next + 4 > workbook.Length)
        throw new InvalidDataException("BkHim bitmap is truncated before its declared image size.");
      var type = BinaryPrimitives.ReadUInt16LittleEndian(workbook[next..]);
      var continuationLength = BinaryPrimitives.ReadUInt16LittleEndian(workbook[(next + 2)..]);
      if (type != _Continue)
        throw new InvalidDataException("BkHim bitmap is truncated: expected a Continue record.");
      var continuationStart = checked(next + 4);
      var continuationEnd = checked(continuationStart + continuationLength);
      if (continuationEnd > workbook.Length)
        throw new InvalidDataException("BkHim Continue record extends past the Workbook stream.");

      var count = Math.Min(blobLength - copied, continuationLength);
      workbook.Slice(continuationStart, count).CopyTo(dib.AsSpan(copied));
      copied += count;
      next = continuationEnd;
    }

    return _ReadDib(dib);
  }

  private static RawImage _ReadDib(ReadOnlySpan<byte> dib) {
    if (dib.Length < 12)
      throw new InvalidDataException("BkHim bitmap is shorter than a BITMAPCOREHEADER.");

    var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
    if (headerSize != 12)
      throw new InvalidDataException($"BkHim bitmap uses unsupported DIB header size {headerSize}; only the BIFF8 12-byte core header is supported.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(dib[6..]);
    var planes = BinaryPrimitives.ReadUInt16LittleEndian(dib[8..]);
    var bpp = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
    if (width == 0 || height == 0 || planes != 1 || bpp != 24)
      throw new InvalidDataException("BkHim BITMAPCOREHEADER is not an uncompressed 24-bit bitmap.");

    var rowBytes = checked(width * 3);
    var stride = checked((rowBytes + 3) & ~3);
    var required = checked(12 + stride * height);
    if (dib.Length < required)
      throw new InvalidDataException("BkHim bitmap pixels are truncated.");

    var pixels = new byte[checked(rowBytes * height)];
    for (var y = 0; y < height; ++y) {
      var sourceY = height - 1 - y;
      dib.Slice(checked(12 + sourceY * stride), rowBytes).CopyTo(pixels.AsSpan(checked(y * rowBytes)));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static void _WriteBof(Stream stream, ushort substreamType) {
    Span<byte> payload = stackalloc byte[16];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 0x0600);
    BinaryPrimitives.WriteUInt16LittleEndian(payload[2..], substreamType);
    BinaryPrimitives.WriteUInt16LittleEndian(payload[4..], 0x2013);
    BinaryPrimitives.WriteUInt16LittleEndian(payload[6..], 0x07CD);
    BinaryPrimitives.WriteUInt32LittleEndian(payload[8..], 0x0000C0C1);
    BinaryPrimitives.WriteUInt32LittleEndian(payload[12..], 0x00000306);
    _WriteRecord(stream, _Bof, payload);
  }

  private static void _WriteRecord(Stream stream, ushort type, ReadOnlySpan<byte> payload) {
    if (payload.Length > _MaximumRecordPayload)
      throw new ArgumentOutOfRangeException(nameof(payload), $"BIFF8 record payload exceeds {_MaximumRecordPayload} bytes.");

    Span<byte> header = stackalloc byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(header, type);
    BinaryPrimitives.WriteUInt16LittleEndian(header[2..], checked((ushort)payload.Length));
    stream.Write(header);
    stream.Write(payload);
  }

  private readonly record struct WorksheetBytes(byte[] Bytes, int IndexIbXfPatch, int DefColWidthOffset);
}
