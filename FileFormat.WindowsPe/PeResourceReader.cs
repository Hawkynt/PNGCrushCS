using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.WindowsPe;

/// <summary>Reads image resources from Windows PE (EXE/DLL) files.</summary>
public static class PeResourceReader {

  private const int _RT_CURSOR = 1;
  private const int _RT_BITMAP = 2;
  private const int _RT_ICON = 3;
  private const int _RT_GROUP_CURSOR = 12;
  private const int _RT_GROUP_ICON = 14;

  private const int _COFF_HEADER_SIZE = 20;
  private const int _RESOURCE_DIRECTORY_SIZE = 16;
  private const int _RESOURCE_ENTRY_SIZE = 8;
  private const int _RESOURCE_DATA_ENTRY_SIZE = 16;
  private const int _SECTION_HEADER_SIZE = 40;
  private const uint _HIGH_BIT = 0x80000000;
  private const int _BITMAP_FILE_HEADER_SIZE = 14;

  public static PeResourceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PE file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PeResourceFile FromStream(Stream stream) {
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

  public static PeResourceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < 64 || data[0] != 0x4D || data[1] != 0x5A)
      throw new InvalidDataException("Not a valid PE file: missing MZ signature.");

    // Read PE offset from e_lfanew at offset 60
    var peOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(60));
    if (peOffset < 0 || peOffset + 4 > data.Length)
      throw new InvalidDataException("Invalid PE offset.");

    // Validate PE signature "PE\0\0"
    if (peOffset + 24 > data.Length)
      throw new InvalidDataException("Data too small for PE header.");

    if (data[peOffset] != 0x50 || data[peOffset + 1] != 0x45 || data[peOffset + 2] != 0 || data[peOffset + 3] != 0)
      throw new InvalidDataException("Not a valid PE file: missing PE\\0\\0 signature.");

    // COFF header starts right after PE signature
    var coffOffset = peOffset + 4;
    if (coffOffset + _COFF_HEADER_SIZE > data.Length)
      throw new InvalidDataException("Data too small for COFF header.");

    var numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(coffOffset + 2));
    var sizeOfOptionalHeader = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(coffOffset + 16));

    // Optional header follows COFF header
    var optionalOffset = coffOffset + _COFF_HEADER_SIZE;
    if (sizeOfOptionalHeader == 0)
      return new PeResourceFile(); // No optional header means no data directories

    if (optionalOffset + sizeOfOptionalHeader > data.Length)
      throw new InvalidDataException("Data too small for optional header.");

    var magic = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(optionalOffset));
    int dataDirOffset;
    if (magic == 0x10B) // PE32
      dataDirOffset = optionalOffset + 96;
    else if (magic == 0x20B) // PE32+
      dataDirOffset = optionalOffset + 112;
    else
      throw new InvalidDataException($"Unknown optional header magic: 0x{magic:X4}.");

    // Resource directory is data directory entry index 2 (each entry is 8 bytes: RVA + Size)
    var resourceDirIndex = dataDirOffset + 2 * 8;
    if (resourceDirIndex + 8 > optionalOffset + sizeOfOptionalHeader)
      return new PeResourceFile(); // No resource data directory

    if (resourceDirIndex + 8 > data.Length)
      return new PeResourceFile();

    var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(resourceDirIndex));
    var resourceSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(resourceDirIndex + 4));

    if (resourceRva == 0 || resourceSize == 0)
      return new PeResourceFile(); // No resources

    // Parse section headers to find the section containing the resource RVA
    var sectionTableOffset = optionalOffset + sizeOfOptionalHeader;
    if (!_FindSectionForRva(data, sectionTableOffset, numberOfSections, resourceRva, out var rsrcFileOffset, out _))
      return new PeResourceFile(); // Resource section not found

    if (rsrcFileOffset < 0 || rsrcFileOffset + resourceSize > data.Length)
      return new PeResourceFile(); // Resource data out of bounds

    // Parse the resource directory tree
    var iconResources = new Dictionary<int, (int Offset, int Size)>();
    var groupIconResources = new Dictionary<int, (int Offset, int Size)>();
    var cursorResources = new Dictionary<int, (int Offset, int Size)>();
    var groupCursorResources = new Dictionary<int, (int Offset, int Size)>();
    var bitmapResources = new Dictionary<int, (int Offset, int Size)>();
    var otherResources = new List<(int TypeId, int ResourceId, int Offset, int Size)>();

    _ParseTopLevelDirectory(
      data,
      rsrcFileOffset,
      resourceRva,
      iconResources,
      groupIconResources,
      cursorResources,
      groupCursorResources,
      bitmapResources,
      otherResources
    );

    // Build icon groups (backward-compatible) and unified image resource list
    var groups = new List<PeIconGroup>();
    var imageResources = new List<PeImageResource>();

    foreach (var (groupId, (offset, size)) in groupIconResources) {
      var icoData = _AssembleIco(data, offset, size, iconResources, rsrcFileOffset, resourceRva, isCursor: false);
      if (icoData != null) {
        groups.Add(new PeIconGroup { GroupId = groupId, IsCursor = false, IcoData = icoData });
        imageResources.Add(new PeImageResource {
          ResourceType = PeImageResourceType.Icon,
          ResourceId = groupId,
          Data = icoData,
        });
      }
    }

    foreach (var (groupId, (offset, size)) in groupCursorResources) {
      var curData = _AssembleCur(data, offset, size, cursorResources, rsrcFileOffset, resourceRva);
      if (curData != null) {
        groups.Add(new PeIconGroup { GroupId = groupId, IsCursor = true, IcoData = curData });
        imageResources.Add(new PeImageResource {
          ResourceType = PeImageResourceType.Cursor,
          ResourceId = groupId,
          Data = curData,
        });
      }
    }

    // RT_BITMAP: prepend BITMAPFILEHEADER to produce a complete BMP file
    foreach (var (resId, (offset, size)) in bitmapResources) {
      if (size <= 0 || offset + size > data.Length)
        continue;

      var bmpData = _PrependBitmapFileHeader(data, offset, size);
      imageResources.Add(new PeImageResource {
        ResourceType = PeImageResourceType.Bitmap,
        ResourceId = resId,
        Data = bmpData,
      });
    }

    // Scan other resource types for embedded image signatures (RT_RCDATA, custom types, etc.)
    foreach (var (_, resId, offset, size) in otherResources) {
      if (size <= 0 || offset + size > data.Length)
        continue;

      var formatHint = _DetectImageSignature(data, offset, size);
      if (formatHint == null)
        continue;

      var rawData = new byte[size];
      data.AsSpan(offset, size).CopyTo(rawData);
      imageResources.Add(new PeImageResource {
        ResourceType = PeImageResourceType.EmbeddedImage,
        ResourceId = resId,
        Data = rawData,
        FormatHint = formatHint,
      });
    }

    return new PeResourceFile { IconGroups = groups, ImageResources = imageResources };
  }

  private static bool _FindSectionForRva(
    byte[] data,
    int sectionTableOffset,
    int numberOfSections,
    uint rva,
    out int fileOffset,
    out uint sectionVirtualAddress
  ) {
    fileOffset = 0;
    sectionVirtualAddress = 0;

    for (var i = 0; i < numberOfSections; ++i) {
      var offset = sectionTableOffset + i * _SECTION_HEADER_SIZE;
      if (offset + _SECTION_HEADER_SIZE > data.Length)
        return false;

      var virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8));
      var virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 12));
      var sizeOfRawData = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16));
      var pointerToRawData = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 20));

      // Use the larger of VirtualSize and SizeOfRawData for section extent
      var effectiveSize = Math.Max(virtualSize, sizeOfRawData);
      if (rva >= virtualAddress && rva < virtualAddress + effectiveSize) {
        fileOffset = (int)(rva - virtualAddress + pointerToRawData);
        sectionVirtualAddress = virtualAddress;
        return true;
      }
    }

    return false;
  }

  private static void _ParseTopLevelDirectory(
    byte[] data,
    int rsrcFileOffset,
    uint rsrcRva,
    Dictionary<int, (int Offset, int Size)> iconResources,
    Dictionary<int, (int Offset, int Size)> groupIconResources,
    Dictionary<int, (int Offset, int Size)> cursorResources,
    Dictionary<int, (int Offset, int Size)> groupCursorResources,
    Dictionary<int, (int Offset, int Size)> bitmapResources,
    List<(int TypeId, int ResourceId, int Offset, int Size)> otherResources
  ) {
    // Level 1: resource types
    var entries = _ReadDirectoryEntries(data, rsrcFileOffset);
    foreach (var (id, offset, isDirectory) in entries) {
      if (!isDirectory)
        continue;

      var targetDict = id switch {
        _RT_ICON => iconResources,
        _RT_GROUP_ICON => groupIconResources,
        _RT_CURSOR => cursorResources,
        _RT_GROUP_CURSOR => groupCursorResources,
        _RT_BITMAP => bitmapResources,
        _ => null
      };

      // Level 2: resource name/IDs
      var level2Offset = rsrcFileOffset + offset;
      if (level2Offset < 0 || level2Offset + _RESOURCE_DIRECTORY_SIZE > data.Length)
        continue;

      var level2Entries = _ReadDirectoryEntries(data, level2Offset);
      foreach (var (resId, resOffset, resIsDir) in level2Entries) {
        if (!resIsDir) {
          // Direct data entry (unusual but possible)
          if (!_ReadDataEntry(data, rsrcFileOffset + resOffset, rsrcFileOffset, rsrcRva, out var dataFileOffset, out var dataSize))
            continue;

          if (dataFileOffset >= 0 && dataSize > 0) {
            if (targetDict != null)
              targetDict[resId] = (dataFileOffset, dataSize);
            else
              otherResources.Add((id, resId, dataFileOffset, dataSize));
          }

          continue;
        }

        // Level 3: language variants (pick the first one)
        var level3Offset = rsrcFileOffset + resOffset;
        if (level3Offset < 0 || level3Offset + _RESOURCE_DIRECTORY_SIZE > data.Length)
          continue;

        var level3Entries = _ReadDirectoryEntries(data, level3Offset);
        foreach (var (_, langOffset, langIsDir) in level3Entries) {
          if (langIsDir)
            continue; // Shouldn't happen at level 3

          if (!_ReadDataEntry(data, rsrcFileOffset + langOffset, rsrcFileOffset, rsrcRva, out var dataFileOffset, out var dataSize))
            continue;

          if (dataFileOffset >= 0 && dataSize > 0) {
            if (targetDict != null)
              targetDict[resId] = (dataFileOffset, dataSize);
            else
              otherResources.Add((id, resId, dataFileOffset, dataSize));

            break; // Take first language variant
          }
        }
      }
    }
  }

  private static List<(int Id, int Offset, bool IsDirectory)> _ReadDirectoryEntries(byte[] data, int directoryFileOffset) {
    var result = new List<(int, int, bool)>();

    if (directoryFileOffset + _RESOURCE_DIRECTORY_SIZE > data.Length)
      return result;

    var namedEntryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(directoryFileOffset + 12));
    var idEntryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(directoryFileOffset + 14));
    var totalEntries = namedEntryCount + idEntryCount;

    var entryOffset = directoryFileOffset + _RESOURCE_DIRECTORY_SIZE;
    for (var i = 0; i < totalEntries; ++i) {
      var currentOffset = entryOffset + i * _RESOURCE_ENTRY_SIZE;
      if (currentOffset + _RESOURCE_ENTRY_SIZE > data.Length)
        break;

      var nameOrId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(currentOffset));
      var offsetOrDir = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(currentOffset + 4));

      // If high bit of nameOrId is set, it's a named entry (we use 0 as the id)
      var entryId = (nameOrId & _HIGH_BIT) != 0 ? 0 : (int)nameOrId;
      var isDirectory = (offsetOrDir & _HIGH_BIT) != 0;
      var relativeOffset = (int)(offsetOrDir & ~_HIGH_BIT);

      result.Add((entryId, relativeOffset, isDirectory));
    }

    return result;
  }

  private static bool _ReadDataEntry(byte[] data, int entryFileOffset, int rsrcFileOffset, uint rsrcRva, out int dataFileOffset, out int dataSize) {
    dataFileOffset = -1;
    dataSize = 0;

    if (entryFileOffset < 0 || entryFileOffset + _RESOURCE_DATA_ENTRY_SIZE > data.Length)
      return false;

    var dataRva = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryFileOffset));
    dataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryFileOffset + 4));

    // Convert RVA to file offset: data RVA is relative to the section's virtual address
    // rsrcFileOffset corresponds to rsrcRva in the file
    dataFileOffset = (int)(dataRva - rsrcRva) + rsrcFileOffset;

    if (dataFileOffset < 0 || dataFileOffset + dataSize > data.Length) {
      dataFileOffset = -1;
      dataSize = 0;
      return false;
    }

    return true;
  }

  /// <summary>Prepends a 14-byte BITMAPFILEHEADER to DIB data to produce a complete BMP file.</summary>
  private static byte[] _PrependBitmapFileHeader(byte[] data, int dibOffset, int dibSize) {
    var bmpSize = _BITMAP_FILE_HEADER_SIZE + dibSize;
    var bmp = new byte[bmpSize];

    // Calculate offset to pixel data
    var bfOffBits = _BITMAP_FILE_HEADER_SIZE;
    if (dibSize >= 4) {
      var biSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(dibOffset));
      bfOffBits += biSize;

      // Add color table size
      if (biSize >= 40 && dibSize >= 40) {
        var biBitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(dibOffset + 14));
        var biCompression = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(dibOffset + 16));
        var biClrUsed = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(dibOffset + 32));

        if (biBitCount <= 8)
          bfOffBits += (int)(biClrUsed == 0 ? 1u << biBitCount : biClrUsed) * 4;
        else if (biCompression == 3) // BI_BITFIELDS
          bfOffBits += 12;
        else if (biCompression == 6) // BI_ALPHABITFIELDS
          bfOffBits += 16;
      }
    }

    // Write BITMAPFILEHEADER
    bmp[0] = 0x42; // 'B'
    bmp[1] = 0x4D; // 'M'
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmpSize);
    // bfReserved1 (2 bytes) and bfReserved2 (2 bytes) are already 0
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), bfOffBits);

    // Copy DIB data
    data.AsSpan(dibOffset, dibSize).CopyTo(bmp.AsSpan(_BITMAP_FILE_HEADER_SIZE));

    return bmp;
  }

  /// <summary>Detects known image file signatures in resource data.</summary>
  internal static string? _DetectImageSignature(byte[] data, int offset, int size) {
    if (size < 4)
      return null;

    var end = Math.Min(offset + size, data.Length);
    if (end - offset < 4)
      return null;

    // PNG: 89 50 4E 47 0D 0A 1A 0A
    if (size >= 8
        && data[offset] == 0x89 && data[offset + 1] == 0x50
        && data[offset + 2] == 0x4E && data[offset + 3] == 0x47
        && data[offset + 4] == 0x0D && data[offset + 5] == 0x0A
        && data[offset + 6] == 0x1A && data[offset + 7] == 0x0A)
      return "png";

    // JPEG: FF D8 FF
    if (data[offset] == 0xFF && data[offset + 1] == 0xD8 && data[offset + 2] == 0xFF)
      return "jpeg";

    // GIF: GIF87a or GIF89a
    if (size >= 6
        && data[offset] == 0x47 && data[offset + 1] == 0x49 && data[offset + 2] == 0x46
        && data[offset + 3] == 0x38
        && (data[offset + 4] == 0x37 || data[offset + 4] == 0x39)
        && data[offset + 5] == 0x61)
      return "gif";

    // BMP: BM
    if (data[offset] == 0x42 && data[offset + 1] == 0x4D)
      return "bmp";

    // TIFF LE: II 2A 00
    if (data[offset] == 0x49 && data[offset + 1] == 0x49
        && data[offset + 2] == 0x2A && data[offset + 3] == 0x00)
      return "tiff";

    // TIFF BE: MM 00 2A
    if (data[offset] == 0x4D && data[offset + 1] == 0x4D
        && data[offset + 2] == 0x00 && data[offset + 3] == 0x2A)
      return "tiff";

    // WebP: RIFF....WEBP
    if (size >= 12
        && data[offset] == 0x52 && data[offset + 1] == 0x49
        && data[offset + 2] == 0x46 && data[offset + 3] == 0x46
        && data[offset + 8] == 0x57 && data[offset + 9] == 0x45
        && data[offset + 10] == 0x42 && data[offset + 11] == 0x50)
      return "webp";

    // ICO/CUR: 00 00 01 00 (ICO) or 00 00 02 00 (CUR)
    if (data[offset] == 0x00 && data[offset + 1] == 0x00
        && (data[offset + 2] == 0x01 || data[offset + 2] == 0x02)
        && data[offset + 3] == 0x00)
      return data[offset + 2] == 0x01 ? "ico" : "cur";

    return null;
  }

  private static byte[]? _AssembleIco(
    byte[] data,
    int grpOffset,
    int grpSize,
    Dictionary<int, (int Offset, int Size)> iconResources,
    int rsrcFileOffset,
    uint rsrcRva,
    bool isCursor
  ) {
    // GRPICONDIR layout:
    //   Reserved (2 bytes) = 0
    //   Type (2 bytes) = 1 (icon) or 2 (cursor)
    //   Count (2 bytes) = number of entries
    //   GRPICONDIRENTRY[Count] (14 bytes each):
    //     Width (1), Height (1), ColorCount (1), Reserved (1),
    //     Planes (2), BitCount (2), BytesInRes (4), Id (2)   <-- Id instead of ImageOffset

    if (grpSize < 6)
      return null;

    if (grpOffset + grpSize > data.Length)
      return null;

    var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(grpOffset + 4));

    if (count == 0)
      return null;

    var grpEntrySize = 14; // GRPICONDIRENTRY is 14 bytes (last field is ushort Id)
    if (grpSize < 6 + count * grpEntrySize)
      return null;

    // Calculate total ICO file size
    // ICO header: 6 bytes
    // ICO directory: count * 16 bytes (ICONDIRENTRY has uint ImageOffset instead of ushort Id)
    var icoHeaderSize = 6;

    // First pass: compute total data size and collect entries
    var entries = new List<(byte Width, byte Height, byte ColorCount, byte Reserved, ushort Planes, ushort BitCount, int BytesInRes, int ResourceId, int ActualDataOffset, int ActualDataSize)>();
    var totalDataSize = 0;

    for (var i = 0; i < count; ++i) {
      var entryBase = grpOffset + 6 + i * grpEntrySize;
      var width = data[entryBase];
      var height = data[entryBase + 1];
      var colorCount = data[entryBase + 2];
      var reserved = data[entryBase + 3];
      var planes = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase + 4));
      var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase + 6));
      var bytesInRes = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entryBase + 8));
      var resourceId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase + 12));

      // Find the corresponding RT_ICON resource
      if (!iconResources.TryGetValue(resourceId, out var iconEntry))
        continue; // Skip entries with missing resources

      entries.Add((width, height, colorCount, reserved, planes, bitCount, bytesInRes, resourceId, iconEntry.Offset, iconEntry.Size));
      totalDataSize += iconEntry.Size;
    }

    if (entries.Count == 0)
      return null;

    // Build the ICO file
    var icoSize = icoHeaderSize + entries.Count * 16 + totalDataSize;
    var ico = new byte[icoSize];

    // Write ICO header
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(0), 0);                         // Reserved
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1);                          // Type = Icon
    BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), (ushort)entries.Count);      // Count

    // Write ICO directory entries and copy image data
    var currentDataOffset = icoHeaderSize + entries.Count * 16;
    for (var i = 0; i < entries.Count; ++i) {
      var e = entries[i];
      var dirOffset = icoHeaderSize + i * 16;

      ico[dirOffset] = e.Width;
      ico[dirOffset + 1] = e.Height;
      ico[dirOffset + 2] = e.ColorCount;
      ico[dirOffset + 3] = 0; // Reserved
      BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(dirOffset + 4), e.Planes);
      BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(dirOffset + 6), e.BitCount);
      BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(dirOffset + 8), e.ActualDataSize);
      BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(dirOffset + 12), currentDataOffset);

      // Copy icon data
      data.AsSpan(e.ActualDataOffset, e.ActualDataSize).CopyTo(ico.AsSpan(currentDataOffset));
      currentDataOffset += e.ActualDataSize;
    }

    return ico;
  }

  private static byte[]? _AssembleCur(
    byte[] data,
    int grpOffset,
    int grpSize,
    Dictionary<int, (int Offset, int Size)> cursorResources,
    int rsrcFileOffset,
    uint rsrcRva
  ) {
    // GRPCURSORDIRENTRY is 14 bytes like GRPICONDIRENTRY:
    //   Width (2 bytes LE), Height (2 bytes LE) -- note: widths/heights are WORDs for cursors
    //   Planes (2), BitCount (2), BytesInRes (4), Id (2)
    // RT_CURSOR resources have a 4-byte hotspot header (HotspotX:2, HotspotY:2) prepended to the DIB

    if (grpSize < 6 || grpOffset + grpSize > data.Length)
      return null;

    var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(grpOffset + 4));
    if (count == 0)
      return null;

    var grpEntrySize = 14;
    if (grpSize < 6 + count * grpEntrySize)
      return null;

    var icoHeaderSize = 6;
    var entries = new List<(ushort HotspotX, ushort HotspotY, byte Width, byte Height, byte ColorCount, int ActualDataOffset, int ActualDataSize, ushort Planes, ushort BitCount)>();
    var totalDataSize = 0;

    for (var i = 0; i < count; ++i) {
      var entryBase = grpOffset + 6 + i * grpEntrySize;

      // Cursor group entry: Width(2), Height(2) are WORDs
      var width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase));
      var height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase + 2));
      var planes = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase + 4));
      var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase + 6));
      var resourceId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryBase + 12));

      if (!cursorResources.TryGetValue(resourceId, out var curEntry))
        continue;

      // RT_CURSOR data has a 4-byte hotspot header
      if (curEntry.Size < 4)
        continue;

      var hotspotX = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(curEntry.Offset));
      var hotspotY = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(curEntry.Offset + 2));
      var dibSize = curEntry.Size - 4;
      var dibOffset = curEntry.Offset + 4;

      // For ICO/CUR directory entry, width/height must fit in a byte (0=256)
      var bWidth = width >= 256 ? (byte)0 : (byte)width;
      // Cursor heights in PE are doubled (includes AND mask), so halve for ICO directory
      var actualHeight = height / 2;
      var bHeight = actualHeight >= 256 ? (byte)0 : (byte)actualHeight;

      entries.Add((hotspotX, hotspotY, bWidth, bHeight, 0, dibOffset, dibSize, planes, bitCount));
      totalDataSize += dibSize;
    }

    if (entries.Count == 0)
      return null;

    // Build CUR file (Type=2)
    var curSize = icoHeaderSize + entries.Count * 16 + totalDataSize;
    var cur = new byte[curSize];

    BinaryPrimitives.WriteUInt16LittleEndian(cur.AsSpan(0), 0);                          // Reserved
    BinaryPrimitives.WriteUInt16LittleEndian(cur.AsSpan(2), 2);                           // Type = Cursor
    BinaryPrimitives.WriteUInt16LittleEndian(cur.AsSpan(4), (ushort)entries.Count);       // Count

    var currentDataOffset = icoHeaderSize + entries.Count * 16;
    for (var i = 0; i < entries.Count; ++i) {
      var e = entries[i];
      var dirOffset = icoHeaderSize + i * 16;

      cur[dirOffset] = e.Width;
      cur[dirOffset + 1] = e.Height;
      cur[dirOffset + 2] = e.ColorCount;
      cur[dirOffset + 3] = 0;
      BinaryPrimitives.WriteUInt16LittleEndian(cur.AsSpan(dirOffset + 4), e.HotspotX);     // HotspotX for cursors
      BinaryPrimitives.WriteUInt16LittleEndian(cur.AsSpan(dirOffset + 6), e.HotspotY);     // HotspotY for cursors
      BinaryPrimitives.WriteInt32LittleEndian(cur.AsSpan(dirOffset + 8), e.ActualDataSize);
      BinaryPrimitives.WriteInt32LittleEndian(cur.AsSpan(dirOffset + 12), currentDataOffset);

      data.AsSpan(e.ActualDataOffset, e.ActualDataSize).CopyTo(cur.AsSpan(currentDataOffset));
      currentDataOffset += e.ActualDataSize;
    }

    return cur;
  }
}
