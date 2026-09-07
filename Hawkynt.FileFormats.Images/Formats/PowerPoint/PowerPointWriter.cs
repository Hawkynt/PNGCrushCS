using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.PowerPoint;

/// <summary>Writes the picture represented by this format into a PowerPoint-style Pictures stream.</summary>
/// <remarks>
/// The image contract of <see cref="PowerPointFile"/> is the picture carried by a legacy PowerPoint
/// compound document, not the slide model around it. This writer therefore creates a structurally
/// valid Microsoft Compound File Binary container with a <c>Pictures</c> stream whose first entry is
/// one OfficeArt PNG BLIP. It does not invent slides, text, transitions or any other presentation
/// content that the in-memory model cannot represent.
/// <para/>
/// The Pictures stream is deliberately allocated from the regular FAT even for tiny images. CFB uses
/// its mini stream below 4096 bytes, while the compatibility reader this format models begins its
/// OfficeArt walk at physical offset 512. Padding the stream's declared size to that cutoff makes its
/// first sector both a standards-compliant regular stream and the first sector after the CFB header.
/// </remarks>
public static class PowerPointWriter {

  private const int _SECTOR_SIZE = 512;
  private const int _FAT_ENTRIES_PER_SECTOR = _SECTOR_SIZE / sizeof(uint);
  private const int _HEADER_DIFAT_ENTRIES = 109;
  private const int _DIFAT_ENTRIES_PER_SECTOR = _FAT_ENTRIES_PER_SECTOR - 1;
  private const int _MINI_STREAM_CUTOFF = 4096;

  private const uint _DIFAT_SECTOR = 0xFFFFFFFC;
  private const uint _FAT_SECTOR = 0xFFFFFFFD;
  private const uint _END_OF_CHAIN = 0xFFFFFFFE;
  private const uint _FREE_SECTOR = 0xFFFFFFFF;
  private const uint _NO_STREAM = 0xFFFFFFFF;

  /// <summary>Serializes a picture as a CFB document carrying one OfficeArt PNG BLIP.</summary>
  public static byte[] ToBytes(PowerPointFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new InvalidDataException($"A PowerPoint picture needs positive dimensions, not {file.Width}x{file.Height}.");

    var pixelLength = checked(file.Width * file.Height * 3);
    if (file.PixelData is not { } pixels || pixels.Length < pixelLength)
      throw new InvalidDataException($"PowerPoint pixel data is truncated: expected {pixelLength} RGB bytes.");

    var png = PngWriter.ToBytes(PngFile.FromRawImage(new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels[..pixelLength],
    }));

    return _BuildCompoundFile(_BuildPngBlip(png));
  }

  /// <summary>Builds the one-UID OfficeArtBlipPNG record defined by MS-ODRAW.</summary>
  private static byte[] _BuildPngBlip(ReadOnlySpan<byte> png) {
    var payloadLength = checked(png.Length + PowerPointFile.BlipPrefixSize);
    var result = new byte[checked(PowerPointFile.RecordHeaderSize + payloadLength)];

    BinaryPrimitives.WriteUInt16LittleEndian(result, PowerPointFile.PngBlipVersionAndInstance);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), PowerPointFile.PngBlipType);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)payloadLength));

    _Md4(png).CopyTo(result, PowerPointFile.RecordHeaderSize);
    result[PowerPointFile.RecordHeaderSize + 16] = 0xFF;
    png.CopyTo(result.AsSpan(PowerPointFile.RecordHeaderSize + PowerPointFile.BlipPrefixSize));
    return result;
  }

  /// <summary>Wraps the Pictures stream in a version-3, 512-byte-sector CFB container.</summary>
  private static byte[] _BuildCompoundFile(ReadOnlySpan<byte> pictures) {
    var streamLength = Math.Max(_MINI_STREAM_CUTOFF, pictures.Length);
    var dataSectorCount = _DivideRoundUp(streamLength, _SECTOR_SIZE);

    var fatSectorCount = 0;
    var difatSectorCount = 0;
    while (true) {
      var totalSectorCount = checked(dataSectorCount + 1 + fatSectorCount + difatSectorCount);
      var neededFatSectors = _DivideRoundUp(totalSectorCount, _FAT_ENTRIES_PER_SECTOR);
      var neededDifatSectors = neededFatSectors <= _HEADER_DIFAT_ENTRIES
        ? 0
        : _DivideRoundUp(neededFatSectors - _HEADER_DIFAT_ENTRIES, _DIFAT_ENTRIES_PER_SECTOR);

      if (neededFatSectors == fatSectorCount && neededDifatSectors == difatSectorCount)
        break;

      fatSectorCount = neededFatSectors;
      difatSectorCount = neededDifatSectors;
    }

    var directorySector = dataSectorCount;
    var firstDifatSector = difatSectorCount == 0 ? -1 : checked(directorySector + 1);
    var firstFatSector = checked(directorySector + 1 + difatSectorCount);
    var totalSectors = checked(dataSectorCount + 1 + difatSectorCount + fatSectorCount);
    var totalBytes = checked((long)PowerPointFile.ScanStart + (long)totalSectors * _SECTOR_SIZE);
    if (totalBytes > int.MaxValue)
      throw new InvalidDataException("The PowerPoint image is too large for a version-3 compound file held in one byte array.");

    var result = new byte[(int)totalBytes];
    _WriteHeader(result, fatSectorCount, directorySector, firstDifatSector, difatSectorCount, firstFatSector);

    pictures.CopyTo(result.AsSpan(PowerPointFile.ScanStart));
    _WriteDirectory(result.AsSpan(_SectorOffset(directorySector), _SECTOR_SIZE), streamLength);

    if (difatSectorCount != 0)
      _WriteDifat(result, firstDifatSector, difatSectorCount, firstFatSector, fatSectorCount);

    _WriteFat(result, dataSectorCount, directorySector, firstDifatSector, difatSectorCount, firstFatSector, fatSectorCount, totalSectors);
    return result;
  }

  private static void _WriteHeader(
    Span<byte> result,
    int fatSectorCount,
    int directorySector,
    int firstDifatSector,
    int difatSectorCount,
    int firstFatSector) {

    PowerPointFile.Signature.CopyTo(result);
    BinaryPrimitives.WriteUInt16LittleEndian(result[24..], 0x003E); // CFB minor version 3.62
    BinaryPrimitives.WriteUInt16LittleEndian(result[26..], 0x0003); // 512-byte-sector major version
    BinaryPrimitives.WriteUInt16LittleEndian(result[28..], 0xFFFE); // little-endian byte order
    BinaryPrimitives.WriteUInt16LittleEndian(result[30..], 0x0009); // 2^9 = 512-byte sectors
    BinaryPrimitives.WriteUInt16LittleEndian(result[32..], 0x0006); // 2^6 = 64-byte mini sectors
    BinaryPrimitives.WriteUInt32LittleEndian(result[40..], 0);      // unused in version 3
    BinaryPrimitives.WriteUInt32LittleEndian(result[44..], checked((uint)fatSectorCount));
    BinaryPrimitives.WriteUInt32LittleEndian(result[48..], checked((uint)directorySector));
    BinaryPrimitives.WriteUInt32LittleEndian(result[52..], 0);      // no transactions
    BinaryPrimitives.WriteUInt32LittleEndian(result[56..], _MINI_STREAM_CUTOFF);
    BinaryPrimitives.WriteUInt32LittleEndian(result[60..], _END_OF_CHAIN); // no mini FAT
    BinaryPrimitives.WriteUInt32LittleEndian(result[64..], 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result[68..], firstDifatSector < 0 ? _END_OF_CHAIN : checked((uint)firstDifatSector));
    BinaryPrimitives.WriteUInt32LittleEndian(result[72..], checked((uint)difatSectorCount));

    for (var i = 0; i < _HEADER_DIFAT_ENTRIES; ++i) {
      var sector = i < fatSectorCount ? checked((uint)(firstFatSector + i)) : _FREE_SECTOR;
      BinaryPrimitives.WriteUInt32LittleEndian(result[(76 + i * sizeof(uint))..], sector);
    }
  }

  private static void _WriteDirectory(Span<byte> directory, int streamLength) {
    _WriteDirectoryEntry(directory[..128], "Root Entry", 5, _END_OF_CHAIN, 0, 1);
    _WriteDirectoryEntry(directory.Slice(128, 128), "Pictures", 2, 0, checked((ulong)streamLength), _NO_STREAM);
  }

  private static void _WriteDirectoryEntry(
    Span<byte> entry,
    string name,
    byte type,
    uint startSector,
    ulong size,
    uint child) {

    var encodedName = Encoding.Unicode.GetBytes(name + '\0');
    if (encodedName.Length > 64)
      throw new InvalidDataException($"Compound-file directory name '{name}' is too long.");

    encodedName.CopyTo(entry);
    BinaryPrimitives.WriteUInt16LittleEndian(entry[64..], checked((ushort)encodedName.Length));
    entry[66] = type;
    entry[67] = 1; // black node in the directory red/black tree
    BinaryPrimitives.WriteUInt32LittleEndian(entry[68..], _NO_STREAM);
    BinaryPrimitives.WriteUInt32LittleEndian(entry[72..], _NO_STREAM);
    BinaryPrimitives.WriteUInt32LittleEndian(entry[76..], child);
    BinaryPrimitives.WriteUInt32LittleEndian(entry[116..], startSector);
    BinaryPrimitives.WriteUInt64LittleEndian(entry[120..], size);
  }

  private static void _WriteDifat(
    Span<byte> result,
    int firstDifatSector,
    int difatSectorCount,
    int firstFatSector,
    int fatSectorCount) {

    var fatIndex = _HEADER_DIFAT_ENTRIES;
    for (var i = 0; i < difatSectorCount; ++i) {
      var sector = result.Slice(_SectorOffset(firstDifatSector + i), _SECTOR_SIZE);
      for (var j = 0; j < _DIFAT_ENTRIES_PER_SECTOR; ++j) {
        var value = fatIndex < fatSectorCount
          ? checked((uint)(firstFatSector + fatIndex++))
          : _FREE_SECTOR;
        BinaryPrimitives.WriteUInt32LittleEndian(sector[(j * sizeof(uint))..], value);
      }

      var next = i + 1 < difatSectorCount ? checked((uint)(firstDifatSector + i + 1)) : _END_OF_CHAIN;
      BinaryPrimitives.WriteUInt32LittleEndian(sector[(_SECTOR_SIZE - sizeof(uint))..], next);
    }
  }

  private static void _WriteFat(
    Span<byte> result,
    int dataSectorCount,
    int directorySector,
    int firstDifatSector,
    int difatSectorCount,
    int firstFatSector,
    int fatSectorCount,
    int totalSectors) {

    for (var fat = 0; fat < fatSectorCount; ++fat) {
      var sector = result.Slice(_SectorOffset(firstFatSector + fat), _SECTOR_SIZE);
      for (var slot = 0; slot < _FAT_ENTRIES_PER_SECTOR; ++slot) {
        var index = checked(fat * _FAT_ENTRIES_PER_SECTOR + slot);
        uint value;
        if (index >= totalSectors)
          value = _FREE_SECTOR;
        else if (index < dataSectorCount)
          value = index + 1 < dataSectorCount ? checked((uint)(index + 1)) : _END_OF_CHAIN;
        else if (index == directorySector)
          value = _END_OF_CHAIN;
        else if (difatSectorCount != 0 && index >= firstDifatSector && index < firstDifatSector + difatSectorCount)
          value = _DIFAT_SECTOR;
        else if (index >= firstFatSector && index < firstFatSector + fatSectorCount)
          value = _FAT_SECTOR;
        else
          value = _FREE_SECTOR;

        BinaryPrimitives.WriteUInt32LittleEndian(sector[(slot * sizeof(uint))..], value);
      }
    }
  }

  private static int _SectorOffset(int sector) => checked(PowerPointFile.ScanStart + sector * _SECTOR_SIZE);

  private static int _DivideRoundUp(int value, int divisor)
    => checked((value + divisor - 1) / divisor);

  /// <summary>RFC 1320 MD4, used by OfficeArt as the BLIP UID.</summary>
  private static byte[] _Md4(ReadOnlySpan<byte> data) {
    var a = 0x67452301u;
    var b = 0xEFCDAB89u;
    var c = 0x98BADCFEu;
    var d = 0x10325476u;

    var completeBytes = data.Length & ~63;
    for (var offset = 0; offset < completeBytes; offset += 64)
      _Md4Block(data.Slice(offset, 64), ref a, ref b, ref c, ref d);

    Span<byte> tail = stackalloc byte[128];
    var remainder = data[completeBytes..];
    remainder.CopyTo(tail);
    tail[remainder.Length] = 0x80;
    var tailLength = remainder.Length < 56 ? 64 : 128;
    BinaryPrimitives.WriteUInt64LittleEndian(tail[(tailLength - 8)..], checked((ulong)data.Length * 8));

    for (var offset = 0; offset < tailLength; offset += 64)
      _Md4Block(tail.Slice(offset, 64), ref a, ref b, ref c, ref d);

    var digest = new byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(digest, a);
    BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(4), b);
    BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(8), c);
    BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(12), d);
    return digest;
  }

  private static void _Md4Block(ReadOnlySpan<byte> block, ref uint a, ref uint b, ref uint c, ref uint d) {
    Span<uint> x = stackalloc uint[16];
    for (var i = 0; i < x.Length; ++i)
      x[i] = BinaryPrimitives.ReadUInt32LittleEndian(block[(i * sizeof(uint))..]);

    var aa = a;
    var bb = b;
    var cc = c;
    var dd = d;

    _F(ref a, b, c, d, x[0], 3); _F(ref d, a, b, c, x[1], 7); _F(ref c, d, a, b, x[2], 11); _F(ref b, c, d, a, x[3], 19);
    _F(ref a, b, c, d, x[4], 3); _F(ref d, a, b, c, x[5], 7); _F(ref c, d, a, b, x[6], 11); _F(ref b, c, d, a, x[7], 19);
    _F(ref a, b, c, d, x[8], 3); _F(ref d, a, b, c, x[9], 7); _F(ref c, d, a, b, x[10], 11); _F(ref b, c, d, a, x[11], 19);
    _F(ref a, b, c, d, x[12], 3); _F(ref d, a, b, c, x[13], 7); _F(ref c, d, a, b, x[14], 11); _F(ref b, c, d, a, x[15], 19);

    _G(ref a, b, c, d, x[0], 3); _G(ref d, a, b, c, x[4], 5); _G(ref c, d, a, b, x[8], 9); _G(ref b, c, d, a, x[12], 13);
    _G(ref a, b, c, d, x[1], 3); _G(ref d, a, b, c, x[5], 5); _G(ref c, d, a, b, x[9], 9); _G(ref b, c, d, a, x[13], 13);
    _G(ref a, b, c, d, x[2], 3); _G(ref d, a, b, c, x[6], 5); _G(ref c, d, a, b, x[10], 9); _G(ref b, c, d, a, x[14], 13);
    _G(ref a, b, c, d, x[3], 3); _G(ref d, a, b, c, x[7], 5); _G(ref c, d, a, b, x[11], 9); _G(ref b, c, d, a, x[15], 13);

    _H(ref a, b, c, d, x[0], 3); _H(ref d, a, b, c, x[8], 9); _H(ref c, d, a, b, x[4], 11); _H(ref b, c, d, a, x[12], 15);
    _H(ref a, b, c, d, x[2], 3); _H(ref d, a, b, c, x[10], 9); _H(ref c, d, a, b, x[6], 11); _H(ref b, c, d, a, x[14], 15);
    _H(ref a, b, c, d, x[1], 3); _H(ref d, a, b, c, x[9], 9); _H(ref c, d, a, b, x[5], 11); _H(ref b, c, d, a, x[13], 15);
    _H(ref a, b, c, d, x[3], 3); _H(ref d, a, b, c, x[11], 9); _H(ref c, d, a, b, x[7], 11); _H(ref b, c, d, a, x[15], 15);

    a = unchecked(a + aa);
    b = unchecked(b + bb);
    c = unchecked(c + cc);
    d = unchecked(d + dd);
  }

  private static void _F(ref uint a, uint b, uint c, uint d, uint x, int shift)
    => a = BitOperations.RotateLeft(unchecked(a + ((b & c) | (~b & d)) + x), shift);

  private static void _G(ref uint a, uint b, uint c, uint d, uint x, int shift)
    => a = BitOperations.RotateLeft(unchecked(a + ((b & c) | (b & d) | (c & d)) + x + 0x5A827999u), shift);

  private static void _H(ref uint a, uint b, uint c, uint d, uint x, int shift)
    => a = BitOperations.RotateLeft(unchecked(a + (b ^ c ^ d) + x + 0x6ED9EBA1u), shift);
}
