using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Png;

namespace FileFormat.Dwg;

/// <summary>Writes an image as the PNG preview of a self-contained AutoCAD R2000 drawing.</summary>
/// <remarks>
/// R2000 keeps its file directory and preview independent from the CAD object database. An image
/// writer therefore needs no CAD entity model: this emits the smallest useful R2000 envelope — an
/// empty class section and an empty object map — and places the source pixels in the standard image
/// block addressed by the file header at <c>0x0D</c>.
/// <para/>
/// The layout follows the Open Design Alliance R13/R14/R2000 file-header, class-section, handle-map
/// and image-block descriptions. The CRC is DWG's reflected CRC-16 with seed <c>0xC0C1</c>.
/// </remarks>
public static class DwgWriter {

  private const int _PreviewHeaderLength = 80;
  private const ushort _Ansi1252CodePage = 30;
  private const ushort _CrcSeed = 0xC0C1;

  private static ReadOnlySpan<byte> _DirectoryEndSentinel => [
    0x95, 0xA0, 0x4E, 0x28, 0x99, 0x82, 0x1A, 0xE5,
    0x5E, 0x41, 0xE0, 0x5F, 0x9D, 0x3A, 0x4D, 0x00,
  ];

  private static ReadOnlySpan<byte> _ClassesStartSentinel => [
    0x8D, 0xA1, 0xC4, 0xB8, 0xC4, 0xA9, 0xF8, 0xC5,
    0xC0, 0xDC, 0xF4, 0x5F, 0xE7, 0xCF, 0xB6, 0x8A,
  ];

  private static ReadOnlySpan<byte> _ClassesEndSentinel => [
    0x72, 0x5E, 0x3B, 0x47, 0x3B, 0x56, 0x07, 0x3A,
    0x3F, 0x23, 0x0B, 0xA0, 0x18, 0x30, 0x49, 0x75,
  ];

  public static byte[] ToBytes(DwgFile file) {
    var thumbnail = file.Thumbnail ?? throw new InvalidDataException("A DWG writer needs a thumbnail image.");
    var png = PngWriter.ToBytes(PngFile.FromRawImage(thumbnail));
    return _AssembleR2000(png);
  }

  private static byte[] _AssembleR2000(ReadOnlySpan<byte> png) {
    var classes = _EmptyClassesSection();
    var handles = _EmptyHandleMap();

    const int recordCount = 2;
    const int directoryStart = 0x15;
    const int locatorSize = 9;
    var directoryLength = directoryStart + 4 + recordCount * locatorSize + 2 + _DirectoryEndSentinel.Length;
    var classesAt = _Align4(directoryLength);
    var handlesAt = _Align4(checked(classesAt + classes.Length));
    var previewAt = _Align4(checked(handlesAt + handles.Length));
    var preview = _PreviewBlock(png, previewAt);

    var result = new byte[checked(previewAt + preview.Length)];
    "AC1015"u8.CopyTo(result);

    result[0x0B] = 15;
    result[0x0C] = 1;
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(DwgFile.ImageSeekerOffset), previewAt);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x13), _Ansi1252CodePage);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(directoryStart), recordCount);

    var locatorAt = directoryStart + 4;
    _WriteLocator(result, locatorAt, 1, classesAt, classes.Length);
    locatorAt += locatorSize;
    _WriteLocator(result, locatorAt, 2, handlesAt, handles.Length);
    locatorAt += locatorSize;

    var directoryCrc = _Crc16(result.AsSpan(0, locatorAt));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(locatorAt), directoryCrc);
    locatorAt += 2;
    _DirectoryEndSentinel.CopyTo(result.AsSpan(locatorAt));

    classes.CopyTo(result.AsSpan(classesAt));
    handles.CopyTo(result.AsSpan(handlesAt));
    preview.CopyTo(result.AsSpan(previewAt));
    return result;
  }

  private static byte[] _EmptyClassesSection() {
    var result = new byte[16 + 4 + 2 + 16];
    _ClassesStartSentinel.CopyTo(result);
    var crc = _Crc16(result.AsSpan(16, 4));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), crc);
    _ClassesEndSentinel.CopyTo(result.AsSpan(22));
    return result;
  }

  private static byte[] _EmptyHandleMap() {
    var result = new byte[8];
    _WriteEmptyHandleBlock(result.AsSpan(0, 4));
    _WriteEmptyHandleBlock(result.AsSpan(4, 4));
    return result;
  }

  private static void _WriteEmptyHandleBlock(Span<byte> destination) {
    BinaryPrimitives.WriteUInt16BigEndian(destination, 2);
    var crc = _Crc16(destination[..2]);
    BinaryPrimitives.WriteUInt16BigEndian(destination[2..], crc);
  }

  private static byte[] _PreviewBlock(ReadOnlySpan<byte> png, int absoluteOffset) {
    var blockLength = checked(1 + 2 * DwgFile.ImageDescriptorSize + _PreviewHeaderLength + png.Length);
    var result = new byte[checked(DwgFile.ImageSentinel.Length + 4 + blockLength + DwgFile.ImageSentinel.Length)];
    DwgFile.ImageSentinel.CopyTo(result);

    var at = DwgFile.ImageSentinel.Length;
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), blockLength);
    at += 4;
    result[at++] = 2;

    var dataAt = DwgFile.ImageSentinel.Length + 4 + 1 + 2 * DwgFile.ImageDescriptorSize;
    var headerAt = checked(absoluteOffset + dataAt);
    var imageAt = checked(headerAt + _PreviewHeaderLength);

    result[at++] = DwgFile.TypeHeaderData;
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), headerAt);
    at += 4;
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), _PreviewHeaderLength);
    at += 4;

    result[at++] = DwgFile.TypePng;
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), imageAt);
    at += 4;
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), png.Length);
    at += 4;

    at += _PreviewHeaderLength;
    png.CopyTo(result.AsSpan(at));

    var end = DwgFile.ImageSentinel.Length + 4 + blockLength;
    var sentinel = DwgFile.ImageSentinel;
    for (var i = 0; i < sentinel.Length; ++i)
      result[end + i] = (byte)~sentinel[i];

    return result;
  }

  private static void _WriteLocator(Span<byte> destination, int at, byte number, int offset, int length) {
    destination[at] = number;
    BinaryPrimitives.WriteInt32LittleEndian(destination[(at + 1)..], offset);
    BinaryPrimitives.WriteInt32LittleEndian(destination[(at + 5)..], length);
  }

  private static int _Align4(int value) => checked((value + 3) & ~3);

  private static ushort _Crc16(ReadOnlySpan<byte> data) {
    var crc = _CrcSeed;
    foreach (var value in data) {
      crc ^= value;
      for (var bit = 0; bit < 8; ++bit)
        crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
    }

    return crc;
  }
}
