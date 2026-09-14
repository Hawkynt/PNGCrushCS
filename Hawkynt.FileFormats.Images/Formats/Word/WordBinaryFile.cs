using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;
using FileFormat.Fpx;
using FileFormat.Png;
using FileFormat.PowerPoint;

namespace FileFormat.Word;

/// <summary>Reads and writes the legacy Word binary document form used by .doc and .dot.</summary>
/// <remarks>
/// The document skeleton is a deliberately tiny one-picture Word binary document. Its WordDocument
/// and 1Table streams were generated once from a controlled one-picture document by LibreOffice
/// 25.2 and retained here as compressed compatibility data; no LibreOffice code is copied and no
/// external executable is needed at runtime. The streams are invariant across source pictures: the
/// picture character, CLX piece table and CHPX/PAPX bin tables point at offset zero of Data. The Data
/// stream is built here from the normative PICF/OfficeArt structures and carries the caller's PNG.
/// This gives Word a real FIB, piece table, formatting tables and inline picture rather than merely
/// hiding image bytes in an OLE stream.
/// </remarks>
internal static class WordBinaryFile {

  private const int _FibFlagsOffset = 10;
  private const ushort _FibFlagDot = 0x0001;
  private const int _PicfSize = 68;

  private static readonly Guid _WordDocument8ClassId = new("00020906-0000-0000-C000-000000000046");
  private static readonly Guid _WordTemplate8ClassId = new("00020907-0000-0000-C000-000000000046");

  // Controlled LibreOffice oracle output, compressed only to avoid 14 KiB of meaningless literal
  // zero/style-table noise in the source. These are file-format data, not translated implementation.
  private const string _WordDocumentZlib =
    "eNp7s5SR0VeBk4WB4YfQfgYwMACTHAwMLEDMx+CcmJ+TmGcBEUUBnCxiDLZ8CD4TA37w/z8/AyuQZoSy2ZDY6KADTM4QYUCh8QGQGiKUwcEaoGIeIL0DSsPAZSUGhnvMDBSDTUrk6duoxsCQhBSU+4DmiDKMAnLBEWD8HgPSxxQZGCYMAveEAeMzBi2d8AyAO5gYbo4mjlEwCkY0YGTgHQ2EkQyAbTwmIP41GhIjBnCGcjAyZ0GzP7Sd/2E0WIYnAPbPhFJEGBkZmFUYElUYDFUYhZcAO0pLTjA4qgDj35rBxoDBIYCBwSmAiUF+wwV9hQ0PbBU3cLArAbHyhAWsKkBsBJQ3nJDBaFDAIMDAYAzkaRgx8BkwAACr9TDg";

  private const string _OneTableZlib =
    "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";

  private static ReadOnlySpan<byte> _InlineShape => [
    0x0F,0x00,0x04,0xF0,0x66,0x00,0x00,0x00,0xB2,0x04,0x0A,0xF0,0x08,0x00,0x00,0x00,
    0x01,0x04,0x00,0x00,0x00,0x0A,0x00,0x00,0xB3,0x00,0x0B,0xF0,0x42,0x00,0x00,0x00,
    0x81,0x00,0x00,0x00,0x00,0x00,0x82,0x00,0x00,0x00,0x00,0x00,0x83,0x00,0x00,0x00,
    0x00,0x00,0x84,0x00,0x00,0x00,0x00,0x00,0x04,0x41,0x01,0x00,0x00,0x00,0x06,0x01,
    0x00,0x00,0x00,0x00,0x3F,0x01,0x00,0x00,0x00,0x00,0x81,0x01,0xFF,0xFF,0xFF,0x00,
    0x83,0x01,0x00,0x00,0x00,0x00,0xBF,0x01,0x10,0x00,0x10,0x00,0xFF,0x01,0x00,0x00,
    0x08,0x00,0x00,0x00,0x10,0xF0,0x04,0x00,0x00,0x00,0x00,0x00,0x00,0x80,
  ];

  internal static WordFile Read(ReadOnlySpan<byte> data) {
    var compound = new CompoundFile(data);
    var word = compound.Streams().FirstOrDefault(pair => pair.Value.Type == CompoundFile.EntryStream
      && pair.Key.Equals("/WordDocument", StringComparison.OrdinalIgnoreCase));
    if (string.IsNullOrEmpty(word.Key))
      throw new InvalidDataException("Word compound file does not contain a WordDocument stream.");

    var wordBytes = compound.Read(word.Value);
    if (wordBytes.Length < 32 || BinaryPrimitives.ReadUInt16LittleEndian(wordBytes) != 0xA5EC)
      throw new InvalidDataException("WordDocument stream does not begin with a valid Word FIB.");

    var tableName = (BinaryPrimitives.ReadUInt16LittleEndian(wordBytes.AsSpan(_FibFlagsOffset)) & 0x0200) != 0
      ? "/1Table"
      : "/0Table";
    if (!compound.Streams().Any(pair => pair.Value.Type == CompoundFile.EntryStream
      && pair.Key.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
      throw new InvalidDataException($"Word compound file is missing the FIB-selected {tableName[1..]} stream.");

    var dataEntry = compound.Streams().FirstOrDefault(pair => pair.Value.Type == CompoundFile.EntryStream
      && pair.Key.Equals("/Data", StringComparison.OrdinalIgnoreCase));
    if (string.IsNullOrEmpty(dataEntry.Key))
      throw new InvalidDataException("Word document contains no Data stream for its picture.");

    var pictureData = compound.Read(dataEntry.Value);
    var pngAt = pictureData.AsSpan().IndexOf(PngFile.Signature);
    if (pngAt < 0)
      throw new InvalidDataException("Word Data stream contains no decodable PNG inline picture.");

    var image = EmbeddedPictureReader.Decode(pictureData.AsSpan(pngAt)).EnsureFormat(PixelFormat.Rgb24);
    var kind = (BinaryPrimitives.ReadUInt16LittleEndian(wordBytes.AsSpan(_FibFlagsOffset)) & _FibFlagDot) != 0
      ? WordOpenXmlKind.LegacyTemplate
      : WordOpenXmlKind.LegacyDocument;
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Images = [image],
      Kind = kind,
    };
  }

  internal static byte[] Write(RawImage source, bool template) {
    var image = source.EnsureFormat(PixelFormat.Rgb24);
    var png = PngWriter.ToBytes(PngFile.FromRawImage(image));
    var uid = PowerPointWriter.ComputeBlipUid(png);

    var wordDocument = _Inflate(_WordDocumentZlib);
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(wordDocument.AsSpan(_FibFlagsOffset));
    flags = template ? (ushort)(flags | _FibFlagDot) : (ushort)(flags & ~_FibFlagDot);
    BinaryPrimitives.WriteUInt16LittleEndian(wordDocument.AsSpan(_FibFlagsOffset), flags);

    var compound = new CompoundFileWriter(template ? _WordTemplate8ClassId : _WordDocument8ClassId);
    compound.AddStream(0, "WordDocument", wordDocument);
    compound.AddStream(0, "1Table", _Inflate(_OneTableZlib));
    compound.AddStream(0, "Data", _BuildData(image, png, uid));
    return compound.Build();
  }

  private static byte[] _BuildData(RawImage image, byte[] png, byte[] uid) {
    const int fbseFixedBody = 36;
    const int blipHeader = 8;
    const int blipPrefix = 17;
    var blipLength = checked(blipHeader + blipPrefix + png.Length);
    var fbseBodyLength = checked(fbseFixedBody + blipLength);
    var result = new byte[checked(_PicfSize + _InlineShape.Length + 8 + fbseBodyLength)];

    BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)result.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), _PicfSize);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 0x0064); // MFPF.MM_SHAPE

    // Fit within a 2x2 inch box. PICF dimensions are signed 16-bit twips; fitting here avoids the
    // historical overflow of the native-pixel-at-96-DPI convention for large source images.
    var scale = Math.Min(2880d / image.Width, 2880d / image.Height);
    var goalWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
    var goalHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(28), checked((short)goalWidth));
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(30), checked((short)goalHeight));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(32), 1000);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(34), 1000);

    _InlineShape.CopyTo(result.AsSpan(_PicfSize));
    var at = _PicfSize + _InlineShape.Length;

    // OfficeArtFBSE containing one inline OfficeArtBlipPNG.
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), 0x0062); // recVer=2, recInstance=6 (PNG)
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at + 2), 0xF007);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 4), checked((uint)fbseBodyLength));
    at += 8;
    result[at] = 0x06;     // btWin32: PNG
    result[at + 1] = 0x06; // btMacOS: PNG
    uid.CopyTo(result, at + 2);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 20), checked((uint)blipLength));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 24), 1); // cRef
    at += fbseFixedBody;

    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at), 0x6E00); // one UID PNG BLIP
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at + 2), PowerPointFile.PngBlipType);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 4), checked((uint)(blipPrefix + png.Length)));
    uid.CopyTo(result, at + 8);
    result[at + 24] = 0xFF;
    png.CopyTo(result, at + 25);
    return result;
  }

  private static byte[] _Inflate(string base64) {
    using var source = new MemoryStream(Convert.FromBase64String(base64), false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress, false);
    using var target = new MemoryStream();
    zlib.CopyTo(target);
    return target.ToArray();
  }
}
