using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Ico;

namespace FileFormat.IconLibrary;

/// <summary>Reads Windows Icon Library (ICL) containers from bytes, streams, or file paths.</summary>
public static class IconLibraryReader {

  private const int _RtIcon = 3;
  private const int _RtGroupIcon = 14;

  public static IconLibraryFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Icon Library file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IconLibraryFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var remaining = stream.Length - stream.Position;
      if (remaining > int.MaxValue)
        throw new InvalidDataException("Icon Library is too large to decode in memory.");

      var data = new byte[checked((int)remaining)];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static IconLibraryFile FromSpan(ReadOnlySpan<byte> data) {
    _Require(data, 0, 0x40, "Icon Library data is too small for a DOS executable header.");
    if (data[0] != (byte)'M' || data[1] != (byte)'Z')
      throw new InvalidDataException("Icon Library must start with an MZ executable header.");

    var newHeaderOffset = BinaryPrimitives.ReadInt32LittleEndian(data[0x3C..]);
    if (newHeaderOffset < 0)
      throw new InvalidDataException("Icon Library has a negative executable-header offset.");

    _Require(data, newHeaderOffset, 2, "Icon Library executable header is truncated.");
    List<IcoFile> icons;
    if (data[newHeaderOffset] == (byte)'P' && data[newHeaderOffset + 1] == (byte)'E') {
      _Require(data, newHeaderOffset, 4, "Icon Library PE signature is truncated.");
      if (data[newHeaderOffset + 2] != 0 || data[newHeaderOffset + 3] != 0)
        throw new InvalidDataException("Icon Library has an invalid PE signature.");
      icons = _ReadPe(data, newHeaderOffset);
    } else if (data[newHeaderOffset] == (byte)'N' && data[newHeaderOffset + 1] == (byte)'E') {
      icons = _ReadNe(data, newHeaderOffset);
    } else {
      throw new InvalidDataException("Icon Library is neither a PE nor an NE Windows library.");
    }

    if (icons.Count == 0)
      throw new InvalidDataException("Icon Library contains no RT_GROUP_ICON resources.");

    var (width, height) = _PreferredDimensions(icons[0]);
    return new() {
      Width = width,
      Height = height,
      Icons = icons,
      RawData = data.ToArray(),
    };
  }

  public static IconLibraryFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static List<IcoFile> _ReadPe(ReadOnlySpan<byte> data, int peOffset) {
    _Require(data, peOffset, 24, "Icon Library PE header is truncated.");

    var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 6)..]);
    var optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(data[(peOffset + 20)..]);
    var optionalOffset = checked(peOffset + 24);
    _Require(data, optionalOffset, optionalHeaderSize, "Icon Library PE optional header is truncated.");

    var magic = BinaryPrimitives.ReadUInt16LittleEndian(data[optionalOffset..]);
    var (numberOfDirectoriesOffset, directoriesOffset) = magic switch {
      0x010B => (92, 96),
      0x020B => (108, 112),
      _ => throw new InvalidDataException($"Icon Library uses unsupported PE optional-header magic 0x{magic:X4}."),
    };

    if (optionalHeaderSize < directoriesOffset + 3 * 8)
      throw new InvalidDataException("Icon Library PE optional header does not contain a resource data directory.");
    if (BinaryPrimitives.ReadUInt32LittleEndian(data[(optionalOffset + numberOfDirectoriesOffset)..]) <= 2)
      throw new InvalidDataException("Icon Library PE declares no resource data directory.");

    var resourceDirectoryOffset = optionalOffset + directoriesOffset + 2 * 8;
    var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(data[resourceDirectoryOffset..]);
    var resourceSizeRaw = BinaryPrimitives.ReadUInt32LittleEndian(data[(resourceDirectoryOffset + 4)..]);
    if (resourceRva == 0 || resourceSizeRaw == 0 || resourceSizeRaw > int.MaxValue)
      throw new InvalidDataException("Icon Library PE has no usable resource section.");

    var sectionTableOffset = checked(optionalOffset + optionalHeaderSize);
    var sectionBytes = checked(sectionCount * 40);
    _Require(data, sectionTableOffset, sectionBytes, "Icon Library PE section table is truncated.");
    var sections = new PeSection[sectionCount];
    for (var i = 0; i < sectionCount; ++i) {
      var offset = sectionTableOffset + i * 40;
      sections[i] = new(
        BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 12)..]),
        BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 8)..]),
        BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 16)..]),
        BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 20)..])
      );
    }

    var layout = new PeLayout(resourceRva, checked((int)resourceSizeRaw), sections);
    _ = _RvaToFileOffset(data, layout, resourceRva, 16);

    var iconResources = _ReadPeNamedResources(data, layout, _RtIcon);
    var groupResources = _ReadPeNamedResources(data, layout, _RtGroupIcon);
    var result = new List<IcoFile>(groupResources.Count);
    foreach (var group in groupResources) {
      var variant = _SelectVariant(group.Variants, null);
      result.Add(_ParseGroup(variant.Data, id => _FindPeIcon(iconResources, id, variant.Language)));
    }

    return result;
  }

  private static List<PeNamedResource> _ReadPeNamedResources(ReadOnlySpan<byte> data, PeLayout layout, int resourceType) {
    var root = _ReadPeDirectory(data, layout, 0);
    uint typeTarget = 0;
    var found = false;
    foreach (var entry in root)
      if (entry.HasIntegerId && entry.IntegerId == resourceType) {
        typeTarget = entry.Target;
        found = true;
        break;
      }

    if (!found)
      return [];
    if ((typeTarget & 0x80000000u) == 0)
      throw new InvalidDataException($"PE resource type {resourceType} points directly at data.");

    var names = _ReadPeDirectory(data, layout, checked((int)(typeTarget & 0x7FFFFFFFu)));
    var result = new List<PeNamedResource>(names.Count);
    foreach (var name in names) {
      if ((name.Target & 0x80000000u) == 0)
        throw new InvalidDataException($"PE resource type {resourceType} has no language directory.");

      var languages = _ReadPeDirectory(data, layout, checked((int)(name.Target & 0x7FFFFFFFu)));
      var variants = new List<PeVariant>(languages.Count);
      foreach (var language in languages) {
        if ((language.Target & 0x80000000u) != 0)
          throw new InvalidDataException($"PE resource type {resourceType} has an unexpected fourth directory level.");

        variants.Add(new(
          language.HasIntegerId ? language.IntegerId : -1,
          _ReadPeResourceData(data, layout, checked((int)language.Target)
        )));
      }

      if (variants.Count != 0)
        result.Add(new(name.HasIntegerId ? name.IntegerId : -1, variants));
    }

    return result;
  }

  private static List<PeDirectoryEntry> _ReadPeDirectory(ReadOnlySpan<byte> data, PeLayout layout, int relativeOffset) {
    var offset = _ResourceFileOffset(data, layout, relativeOffset, 16);
    var namedCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 12)..]);
    var idCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 14)..]);
    var count = namedCount + idCount;
    var byteCount = checked(16 + count * 8);
    offset = _ResourceFileOffset(data, layout, relativeOffset, byteCount);

    var result = new List<PeDirectoryEntry>(count);
    for (var i = 0; i < count; ++i) {
      var entryOffset = offset + 16 + i * 8;
      result.Add(new(
        BinaryPrimitives.ReadUInt32LittleEndian(data[entryOffset..]),
        BinaryPrimitives.ReadUInt32LittleEndian(data[(entryOffset + 4)..])
      ));
    }

    return result;
  }

  private static byte[] _ReadPeResourceData(ReadOnlySpan<byte> data, PeLayout layout, int dataEntryOffset) {
    var offset = _ResourceFileOffset(data, layout, dataEntryOffset, 16);
    var rva = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    var sizeRaw = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
    if (sizeRaw == 0 || sizeRaw > int.MaxValue)
      throw new InvalidDataException("PE icon resource has an invalid size.");

    var size = checked((int)sizeRaw);
    var fileOffset = _RvaToFileOffset(data, layout, rva, size);
    return data.Slice(fileOffset, size).ToArray();
  }

  private static int _ResourceFileOffset(ReadOnlySpan<byte> data, PeLayout layout, int relativeOffset, int size) {
    if (relativeOffset < 0 || size < 0 || relativeOffset > layout.ResourceSize - size)
      throw new InvalidDataException("PE resource directory points outside the declared resource range.");

    var rva = (ulong)layout.ResourceRva + (uint)relativeOffset;
    if (rva > uint.MaxValue)
      throw new InvalidDataException("PE resource directory RVA overflows 32 bits.");

    return _RvaToFileOffset(data, layout, (uint)rva, size);
  }

  private static int _RvaToFileOffset(ReadOnlySpan<byte> data, PeLayout layout, uint rva, int size) {
    foreach (var section in layout.Sections) {
      var span = Math.Max(section.VirtualSize, section.RawSize);
      if (rva < section.VirtualAddress)
        continue;

      var delta = (ulong)rva - section.VirtualAddress;
      if (delta >= span || delta + (uint)size > section.RawSize)
        continue;

      var offset = (ulong)section.RawOffset + delta;
      if (offset > int.MaxValue)
        break;
      var fileOffset = (int)offset;
      if (size <= data.Length && fileOffset >= 0 && fileOffset <= data.Length - size)
        return fileOffset;
    }

    throw new InvalidDataException($"PE resource RVA 0x{rva:X8} does not map to file data.");
  }

  private static List<IcoFile> _ReadNe(ReadOnlySpan<byte> data, int neOffset) {
    _Require(data, neOffset, 0x28, "Icon Library NE header is truncated.");
    var resourceTableRelativeOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[(neOffset + 0x24)..]);
    if (resourceTableRelativeOffset == 0)
      throw new InvalidDataException("Icon Library NE file has no resource table.");

    var resourceTableOffset = checked(neOffset + resourceTableRelativeOffset);
    _Require(data, resourceTableOffset, 2, "Icon Library NE resource table is truncated.");
    var alignmentShift = BinaryPrimitives.ReadUInt16LittleEndian(data[resourceTableOffset..]);
    if (alignmentShift > 15)
      throw new InvalidDataException($"Icon Library NE resource alignment shift {alignmentShift} is too large.");

    var iconResources = new List<NeResource>();
    var groupResources = new List<NeResource>();
    var cursor = resourceTableOffset + 2;
    while (true) {
      _Require(data, cursor, 2, "Icon Library NE resource type table is truncated.");
      var typeRaw = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
      if (typeRaw == 0)
        break;

      _Require(data, cursor, 8, "Icon Library NE resource type entry is truncated.");
      var count = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 2)..]);
      var typeId = (typeRaw & 0x8000) != 0 ? typeRaw & 0x7FFF : -1;
      cursor += 8;

      for (var i = 0; i < count; ++i) {
        _Require(data, cursor, 12, "Icon Library NE resource entry is truncated.");
        var offsetUnits = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        var lengthUnits = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 2)..]);
        var idRaw = BinaryPrimitives.ReadUInt16LittleEndian(data[(cursor + 6)..]);
        var resourceId = (idRaw & 0x8000) != 0 ? idRaw & 0x7FFF : -1;
        var fileOffset = (long)offsetUnits << alignmentShift;
        var byteLength = (long)lengthUnits << alignmentShift;
        if (fileOffset < 0 || byteLength <= 0 || fileOffset > int.MaxValue || byteLength > int.MaxValue)
          throw new InvalidDataException("Icon Library NE resource has an invalid offset or size.");

        _Require(data, checked((int)fileOffset), checked((int)byteLength), "Icon Library NE resource points outside the file.");
        if (typeId is _RtIcon or _RtGroupIcon) {
          var resource = new NeResource(resourceId, data.Slice((int)fileOffset, (int)byteLength).ToArray());
          if (typeId == _RtIcon)
            iconResources.Add(resource);
          else
            groupResources.Add(resource);
        }

        cursor += 12;
      }
    }

    var result = new List<IcoFile>(groupResources.Count);
    foreach (var group in groupResources)
      result.Add(_ParseGroup(group.Data, id => _FindNeIcon(iconResources, id)));

    return result;
  }

  private static byte[] _FindPeIcon(List<PeNamedResource> icons, ushort id, int preferredLanguage) {
    foreach (var icon in icons)
      if (icon.Id == id)
        return _SelectVariant(icon.Variants, preferredLanguage).Data;

    throw new InvalidDataException($"Icon group references missing RT_ICON resource #{id}.");
  }

  private static byte[] _FindNeIcon(List<NeResource> icons, ushort id) {
    foreach (var icon in icons)
      if (icon.Id == id)
        return icon.Data;

    // Windows 3.x documentation describes this field as a one-based resource-table index. Resource
    // compilers normally make that index and the ordinal resource id the same; accepting the index
    // form as a fallback covers old libraries that did not.
    if (id != 0 && id <= icons.Count)
      return icons[id - 1].Data;

    throw new InvalidDataException($"Icon group references missing RT_ICON resource #{id}.");
  }

  private static PeVariant _SelectVariant(List<PeVariant> variants, int? preferredLanguage) {
    if (variants.Count == 0)
      throw new InvalidDataException("PE icon resource has no language variants.");

    if (preferredLanguage is { } language)
      foreach (var variant in variants)
        if (variant.Language == language)
          return variant;

    foreach (var variant in variants)
      if (variant.Language == 0)
        return variant;

    return variants[0];
  }

  private static IcoFile _ParseGroup(ReadOnlySpan<byte> groupData, Func<ushort, byte[]> getIconResource) {
    _Require(groupData, 0, 6, "RT_GROUP_ICON resource is truncated.");
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(groupData);
    var type = BinaryPrimitives.ReadUInt16LittleEndian(groupData[2..]);
    var count = BinaryPrimitives.ReadUInt16LittleEndian(groupData[4..]);
    if (reserved != 0 || type != 1)
      throw new InvalidDataException("RT_GROUP_ICON has an invalid NEWHEADER.");
    if (count == 0)
      throw new InvalidDataException("RT_GROUP_ICON contains no image entries.");

    var directorySize = checked(6 + count * 14);
    _Require(groupData, 0, directorySize, "RT_GROUP_ICON directory entries are truncated.");
    var images = new List<IcoImage>(count);
    for (var i = 0; i < count; ++i) {
      var offset = 6 + i * 14;
      var width = groupData[offset] == 0 ? IcoDib.MaximumSide : groupData[offset];
      var height = groupData[offset + 1] == 0 ? IcoDib.MaximumSide : groupData[offset + 1];
      var bitsPerPixel = (int)BinaryPrimitives.ReadUInt16LittleEndian(groupData[(offset + 6)..]);
      var sizeRaw = BinaryPrimitives.ReadUInt32LittleEndian(groupData[(offset + 8)..]);
      var resourceId = BinaryPrimitives.ReadUInt16LittleEndian(groupData[(offset + 12)..]);
      var resource = getIconResource(resourceId);
      if (sizeRaw == 0 || sizeRaw > resource.Length)
        throw new InvalidDataException($"RT_GROUP_ICON entry #{i} declares {sizeRaw} bytes but RT_ICON #{resourceId} contains {resource.Length}.");

      var imageData = resource.AsSpan(0, checked((int)sizeRaw)).ToArray();
      var format = _IsPng(imageData) ? IcoImageFormat.Png : IcoImageFormat.Bmp;
      if (format == IcoImageFormat.Bmp) {
        bitsPerPixel = bitsPerPixel == 0 ? _ReadDibBitDepth(imageData) : bitsPerPixel;
        imageData = _NormalizeLegacyDib(imageData);
      }

      images.Add(new() {
        Width = width,
        Height = height,
        BitsPerPixel = bitsPerPixel,
        Format = format,
        Data = imageData,
      });
    }

    return new() { Images = images };
  }

  private static int _ReadDibBitDepth(ReadOnlySpan<byte> data) {
    if (data.Length < 12)
      return 0;

    return BinaryPrimitives.ReadUInt32LittleEndian(data) switch {
      12 => BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
      >= 40 when data.Length >= 16 => BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
      _ => 0,
    };
  }

  private static byte[] _NormalizeLegacyDib(byte[] data) {
    if (data.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 12)
      return data;

    var bitDepth = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10));
    var paletteCount = bitDepth <= 8 ? 1 << bitDepth : 0;
    var oldPaletteSize = checked(paletteCount * 3);
    var oldPixelOffset = checked(12 + oldPaletteSize);
    if (oldPixelOffset > data.Length)
      throw new InvalidDataException("BITMAPCOREHEADER icon resource has a truncated colour table.");

    var newPixelOffset = checked(40 + paletteCount * 4);
    var result = new byte[checked(newPixelOffset + data.Length - oldPixelOffset)];
    BinaryPrimitives.WriteUInt32LittleEndian(result, 40);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)));
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), bitDepth);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(20), checked((uint)(data.Length - oldPixelOffset)));

    for (var i = 0; i < paletteCount; ++i)
      data.AsSpan(12 + i * 3, 3).CopyTo(result.AsSpan(40 + i * 4, 3));
    data.AsSpan(oldPixelOffset).CopyTo(result.AsSpan(newPixelOffset));
    return result;
  }

  private static bool _IsPng(ReadOnlySpan<byte> data)
    => data.Length >= 8
       && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G'
       && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

  private static (int Width, int Height) _PreferredDimensions(IcoFile icon) {
    if (icon.Images.Count == 0)
      return (IconLibraryFile.DefaultSize, IconLibraryFile.DefaultSize);

    var best = icon.Images[0];
    for (var i = 1; i < icon.Images.Count; ++i) {
      var candidate = icon.Images[i];
      var candidateArea = candidate.Width * candidate.Height;
      var bestArea = best.Width * best.Height;
      if (candidateArea > bestArea || candidateArea == bestArea && candidate.BitsPerPixel > best.BitsPerPixel)
        best = candidate;
    }

    return (best.Width, best.Height);
  }

  private static void _Require(ReadOnlySpan<byte> data, int offset, int length, string message) {
    if (offset < 0 || length < 0 || length > data.Length || offset > data.Length - length)
      throw new InvalidDataException(message);
  }

  private readonly record struct PeSection(uint VirtualAddress, uint VirtualSize, uint RawSize, uint RawOffset);
  private readonly record struct PeLayout(uint ResourceRva, int ResourceSize, PeSection[] Sections);
  private readonly record struct PeDirectoryEntry(uint Name, uint Target) {
    public bool HasIntegerId => (this.Name & 0x80000000u) == 0;
    public int IntegerId => checked((int)this.Name);
  }
  private sealed record PeNamedResource(int Id, List<PeVariant> Variants);
  private readonly record struct PeVariant(int Language, byte[] Data);
  private readonly record struct NeResource(int Id, byte[] Data);
}
