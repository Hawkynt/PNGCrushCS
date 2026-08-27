using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>Reads HEIF/HEIC files, resolving image items rather than treating the whole mdat as one image.</summary>
public static class HeifReader {

  private const int _MIN_FILE_SIZE = 12;
  private const int _CLAP_PAYLOAD_SIZE = 32;

  private static readonly HashSet<string> _HEIF_BRANDS = new(StringComparer.Ordinal) {
    "heic", "heix", "hevc", "heim", "heis", "hevm", "hevs", "mif1",
  };

  private static readonly HashSet<string> _HEVC_ITEM_TYPES = new(StringComparer.Ordinal) {
    "hvc1", "hev1", "hvc2", "hev2",
  };

  public static HeifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HEIF file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HeifFile FromStream(Stream stream) {
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

  public static HeifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static HeifFile FromSpan(ReadOnlySpan<byte> data) {
    var bytes = data.ToArray();
    var container = _Parse(bytes);

    if (container.Items.Count == 0)
      return _ReadLegacyRawPayload(container, bytes);

    var visible = _VisibleImageItems(container);
    if (visible.Count == 0) {
      var primaryType = container.ItemInfos.TryGetValue(container.PrimaryItemId, out var type) ? type : "unknown";
      throw new NotSupportedException(
        $"HEIF: the primary item {container.PrimaryItemId} has type '{primaryType}', "
        + "but this reader currently decodes directly coded HEVC image items.");
    }

    var images = new HeifImage[visible.Count];
    for (var i = 0; i < visible.Count; ++i)
      images[i] = _DecodeItem(container, bytes, visible[i]);

    var primary = images[0];
    return new() {
      Width = primary.Width,
      Height = primary.Height,
      PixelData = primary.PixelData[..],
      Brand = container.Brand,
      RawImageData = primary.RawImageData[..],
      Images = images,
    };
  }

  /// <summary>Reads the primary image's extent without decoding its HEVC payload.</summary>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> data) {
    try {
      var bytes = data.ToArray();
      var container = _Parse(bytes);

      if (container.Items.Count == 0) {
        var legacy = _LegacyDescriptor(container);
        var legacyWidth = legacy.Width;
        var legacyHeight = legacy.Height;
        if (legacyWidth <= 0 || legacyHeight <= 0)
          return null;
        if (legacy.Aperture != null
            && _TryResolveCleanAperture(
              legacy.Aperture.Value, legacyWidth, legacyHeight,
              out _, out _, out var legacyCleanWidth, out var legacyCleanHeight)) {
          legacyWidth = legacyCleanWidth;
          legacyHeight = legacyCleanHeight;
        }
        return new(legacyWidth, legacyHeight, 24, "Rgb24", _LegacyHasHevcConfiguration(container) ? "HEVC" : "None");
      }

      var visible = _VisibleImageItems(container);
      var itemId = container.PrimaryItemId;
      if (itemId == 0 || !container.ItemInfos.ContainsKey(itemId))
        itemId = visible.FirstOrDefault();

      if (itemId == 0)
        return null;

      var descriptor = _DescribeItem(container, itemId);
      var width = descriptor.CodedWidth;
      var height = descriptor.CodedHeight;
      if (width <= 0 || height <= 0)
        return null;

      if (descriptor.Aperture != null
          && _TryResolveCleanAperture(
            descriptor.Aperture.Value, width, height,
            out _, out _, out var cleanWidth, out var cleanHeight)) {
        width = cleanWidth;
        height = cleanHeight;
      }

      return new(
        width,
        height,
        24,
        "Rgb24",
        descriptor.HevcConfiguration != null ? "HEVC" : "None",
        Math.Max(1, visible.Count));
    } catch {
      return null;
    }
  }

  private static HeifFile _ReadLegacyRawPayload(HeifContainer container, byte[] bytes) {
    if (_LegacyHasHevcConfiguration(container))
      throw new NotSupportedException(
        "HEIF: the file carries an hvcC HEVC configuration but has no iinf item description, "
        + "so the coded item cannot be addressed safely.");

    var descriptor = _LegacyDescriptor(container);
    var width = descriptor.Width;
    var height = descriptor.Height;

    var mdat = container.TopLevelBoxes.FirstOrDefault(box => box.Type == IsoBmffBox.Mdat);
    if (mdat.Size == 0)
      throw new InvalidDataException("HEIF: no image items and no mdat payload were found.");

    var raw = bytes.AsSpan(mdat.PayloadStart, mdat.PayloadLength).ToArray();
    var expected = checked((long)width * height * 3);
    if (expected <= 0 || expected > int.MaxValue || raw.Length != (int)expected)
      throw new NotSupportedException(
        "HEIF: this legacy container has no iinf item description and its mdat is not an Rgb24 raster.");

    var pixels = raw[..];
    if (descriptor.Aperture != null
        && _TryResolveCleanAperture(
          descriptor.Aperture.Value, width, height,
          out var x, out var y, out var cleanWidth, out var cleanHeight)) {
      pixels = _CropRgb24(pixels, width, x, y, cleanWidth, cleanHeight);
      width = cleanWidth;
      height = cleanHeight;
    }

    var image = new HeifImage {
      ItemId = 1,
      ItemType = "raw ",
      IsPrimary = true,
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = raw,
    };

    return new() {
      Width = width,
      Height = height,
      PixelData = pixels[..],
      Brand = container.Brand,
      RawImageData = raw,
      Images = [image],
    };
  }

  private static HeifImage _DecodeItem(HeifContainer container, byte[] bytes, uint itemId) {
    var descriptor = _DescribeItem(container, itemId);
    if (descriptor.HevcConfiguration == null)
      throw new NotSupportedException(
        $"HEIF: image item {itemId} ('{descriptor.ItemType}') has no hvcC property. "
        + "Only directly coded HEVC items are implemented.");

    if (!container.Locations.TryGetValue(itemId, out var location))
      throw new InvalidDataException($"HEIF: image item {itemId} has no iloc entry.");

    var sample = _ReadItemData(container, bytes, location);
    RawImage decoded;
    try {
      decoded = HeifHevcDecoder.Decode(sample, descriptor.HevcConfiguration);
    } catch (NotSupportedException e) {
      throw new NotSupportedException($"HEIF/HEVC item {itemId}: {e.Message}", e);
    }

    var width = decoded.Width;
    var height = decoded.Height;
    var pixels = decoded.PixelData;

    if (descriptor.CodedWidth > 0 && descriptor.CodedHeight > 0) {
      if (width == descriptor.CodedWidth && height == descriptor.CodedHeight) {
        if (descriptor.Aperture != null
            && _TryResolveCleanAperture(
              descriptor.Aperture.Value, width, height,
              out var x, out var y, out var cleanWidth, out var cleanHeight)) {
          pixels = _CropRgb24(pixels, width, x, y, cleanWidth, cleanHeight);
          width = cleanWidth;
          height = cleanHeight;
        }
      } else if (descriptor.Aperture != null
                 && _TryResolveCleanAperture(
                   descriptor.Aperture.Value, descriptor.CodedWidth, descriptor.CodedHeight,
                   out _, out _, out var cleanWidth, out var cleanHeight)
                 && width == cleanWidth && height == cleanHeight) {
      } else {
        throw new InvalidDataException(
          $"HEIF: item {itemId}'s ispe states {descriptor.CodedWidth}x{descriptor.CodedHeight}, "
          + $"but its H.265 sequence parameter set decodes to {width}x{height}.");
      }
    }

    return new() {
      ItemId = itemId,
      ItemType = descriptor.ItemType,
      IsPrimary = itemId == container.PrimaryItemId,
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = sample.ToArray(),
    };
  }

  private static IReadOnlyList<uint> _VisibleImageItems(HeifContainer container) {
    var result = new List<uint>();

    bool IsDecodable(uint id) {
      var type = container.ItemInfos.TryGetValue(id, out var itemType) ? itemType : string.Empty;
      return _HEVC_ITEM_TYPES.Contains(type) || _HasProperty(container, id, IsoBmffBox.HvcC);
    }

    if (container.PrimaryItemId != 0 && IsDecodable(container.PrimaryItemId))
      result.Add(container.PrimaryItemId);

    foreach (var id in container.ItemInfos.Keys.OrderBy(id => id)) {
      if (id == container.PrimaryItemId || container.HiddenImageItems.Contains(id) || !IsDecodable(id))
        continue;
      result.Add(id);
    }

    return result;
  }

  private static bool _HasProperty(HeifContainer container, uint itemId, string type) {
    if (!container.Associations.TryGetValue(itemId, out var associations))
      return false;

    foreach (var association in associations) {
      if (association.PropertyIndex <= 0 || association.PropertyIndex >= container.Properties.Count)
        continue;
      if (container.Properties[association.PropertyIndex].Type == type)
        return true;
    }

    return false;
  }

  private static ItemDescriptor _DescribeItem(HeifContainer container, uint itemId) {
    var itemType = container.ItemInfos.TryGetValue(itemId, out var type) ? type : string.Empty;
    var width = 0;
    var height = 0;
    byte[]? hvcc = null;
    CleanAperture? aperture = null;

    if (container.Associations.TryGetValue(itemId, out var associations)) {
      foreach (var association in associations) {
        if (association.PropertyIndex <= 0 || association.PropertyIndex >= container.Properties.Count)
          throw new InvalidDataException(
            $"HEIF: item {itemId} associates property {association.PropertyIndex}, "
            + $"but ipco contains only {container.Properties.Count - 1} properties.");

        var property = container.Properties[association.PropertyIndex];
        switch (property.Type) {
          case IsoBmffBox.Ispe:
            if (property.Data.Length >= 12) {
              width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(property.Data.AsSpan(4)));
              height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(property.Data.AsSpan(8)));
            }
            break;

          case IsoBmffBox.Clap:
            if (property.Data.Length >= _CLAP_PAYLOAD_SIZE)
              aperture = _ReadCleanAperture(property.Data);
            break;

          case IsoBmffBox.HvcC:
            hvcc = property.Data;
            break;
        }
      }
    }

    return new(itemType, width, height, aperture, hvcc);
  }

  private static bool _LegacyHasHevcConfiguration(HeifContainer container) {
    for (var i = 1; i < container.Properties.Count; ++i)
      if (container.Properties[i].Type == IsoBmffBox.HvcC)
        return true;
    return false;
  }

  private static LegacyDescriptor _LegacyDescriptor(HeifContainer container) {
    var width = 0;
    var height = 0;
    CleanAperture? aperture = null;

    for (var i = 1; i < container.Properties.Count; ++i) {
      var property = container.Properties[i];
      if (property.Type == IsoBmffBox.Ispe && property.Data.Length >= 12) {
        width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(property.Data.AsSpan(4)));
        height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(property.Data.AsSpan(8)));
      } else if (property.Type == IsoBmffBox.Clap && property.Data.Length >= _CLAP_PAYLOAD_SIZE) {
        aperture = _ReadCleanAperture(property.Data);
      }
    }

    return new(width, height, aperture);
  }

  private static byte[] _ReadItemData(HeifContainer container, byte[] bytes, ItemLocation location) {
    if (location.DataReferenceIndex != 0)
      throw new NotSupportedException(
        $"HEIF: item {location.ItemId} uses data_reference_index {location.DataReferenceIndex}; "
        + "external data references are not implemented.");

    var total = 0L;
    foreach (var extent in location.Extents)
      total = checked(total + (long)extent.Length);

    if (total > int.MaxValue)
      throw new InvalidDataException($"HEIF: item {location.ItemId} is too large to materialize.");

    var result = new byte[(int)total];
    var target = 0;

    foreach (var extent in location.Extents) {
      ulong absolute;
      switch (location.ConstructionMethod) {
        case 0:
          absolute = checked(location.BaseOffset + extent.Offset);
          break;

        case 1: {
          if (container.IdatBox == null)
            throw new InvalidDataException(
              $"HEIF: item {location.ItemId} uses iloc construction_method 1 but meta contains no idat box.");
          absolute = checked((ulong)container.IdatBox.Value.PayloadStart + location.BaseOffset + extent.Offset);
          break;
        }

        default:
          throw new NotSupportedException(
            $"HEIF: item {location.ItemId} uses iloc construction_method {location.ConstructionMethod}; "
            + "only file-offset and idat-offset construction are implemented.");
      }

      if (absolute > (ulong)bytes.Length || extent.Length > (ulong)bytes.Length - absolute)
        throw new InvalidDataException(
          $"HEIF: item {location.ItemId} extent [{absolute}, {absolute + extent.Length}) leaves the file.");

      bytes.AsSpan((int)absolute, checked((int)extent.Length)).CopyTo(result.AsSpan(target));
      target += checked((int)extent.Length);
    }

    return result;
  }

  private static HeifContainer _Parse(byte[] bytes) {
    if (bytes.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid HEIF file.");

    var top = _ReadBoxes(bytes, 0, bytes.Length);
    var ftyp = top.FirstOrDefault(box => box.Type == IsoBmffBox.Ftyp);
    if (ftyp.Size == 0)
      throw new InvalidDataException("Missing ftyp box; not a valid ISOBMFF file.");

    if (ftyp.PayloadLength < 4)
      throw new InvalidDataException("HEIF: ftyp is too short to hold a major brand.");

    var brand = Encoding.ASCII.GetString(bytes, ftyp.PayloadStart, 4);
    if (!_HEIF_BRANDS.Contains(brand))
      throw new InvalidDataException($"Unsupported major brand '{brand}'; expected a HEIF brand.");

    var meta = top.FirstOrDefault(box => box.Type == IsoBmffBox.Meta);
    if (meta.Size == 0)
      throw new InvalidDataException("HEIF: the file has no meta box.");
    if (meta.PayloadLength < 4)
      throw new InvalidDataException("HEIF: meta is shorter than its FullBox header.");

    var children = _ReadBoxes(bytes, meta.PayloadStart + 4, meta.End);
    var primary = 0u;
    var itemInfos = new Dictionary<uint, string>();
    var locations = new Dictionary<uint, ItemLocation>();
    var properties = new List<PropertyBox> { new(string.Empty, []) };
    var associations = new Dictionary<uint, List<PropertyAssociation>>();
    var hidden = new HashSet<uint>();

    Box? idat = null;

    foreach (var box in children) {
      switch (box.Type) {
        case IsoBmffBox.Pitm:
          primary = _ParsePrimaryItem(bytes, box);
          break;

        case "iinf":
          _ParseItemInfo(bytes, box, itemInfos);
          break;

        case IsoBmffBox.Iloc:
          _ParseItemLocations(bytes, box, locations);
          break;

        case IsoBmffBox.Iprp:
          _ParseItemProperties(bytes, box, properties, associations);
          break;

        case "iref":
          _ParseItemReferences(bytes, box, hidden);
          break;

        case "idat":
          idat = box;
          break;
      }
    }

    return new(
      brand,
      primary,
      itemInfos,
      locations,
      properties,
      associations,
      hidden,
      top,
      idat);
  }

  private static uint _ParsePrimaryItem(byte[] bytes, Box box) {
    var data = bytes.AsSpan(box.PayloadStart, box.PayloadLength);
    if (data.Length < 6)
      throw new InvalidDataException("HEIF: pitm is truncated.");

    return data[0] == 0
      ? BinaryPrimitives.ReadUInt16BigEndian(data[4..])
      : data.Length >= 8
        ? BinaryPrimitives.ReadUInt32BigEndian(data[4..])
        : throw new InvalidDataException("HEIF: version-1 pitm is truncated.");
  }

  private static void _ParseItemInfo(byte[] bytes, Box box, Dictionary<uint, string> itemInfos) {
    var data = bytes.AsSpan(box.PayloadStart, box.PayloadLength);
    if (data.Length < 6)
      throw new InvalidDataException("HEIF: iinf is truncated.");

    var version = data[0];
    var at = 4;
    var count = version == 0
      ? (uint)_ReadU16(data, ref at)
      : _ReadU32(data, ref at);

    var childStart = box.PayloadStart + at;
    var children = _ReadBoxes(bytes, childStart, box.End);
    foreach (var infe in children) {
      if (infe.Type != "infe")
        continue;

      var payload = bytes.AsSpan(infe.PayloadStart, infe.PayloadLength);
      if (payload.Length < 8)
        throw new InvalidDataException("HEIF: infe is truncated.");

      var infeVersion = payload[0];
      var p = 4;
      uint itemId;
      string itemType;

      if (infeVersion == 2) {
        itemId = _ReadU16(payload, ref p);
        _ = _ReadU16(payload, ref p);
        itemType = _ReadFourCc(payload, ref p);
      } else if (infeVersion == 3) {
        itemId = _ReadU32(payload, ref p);
        _ = _ReadU16(payload, ref p);
        itemType = _ReadFourCc(payload, ref p);
      } else {
        itemId = _ReadU16(payload, ref p);
        itemType = string.Empty;
      }

      itemInfos[itemId] = itemType;
    }

    if ((uint)children.Count < count)
      throw new InvalidDataException(
        $"HEIF: iinf declares {count} item-info entries but contains only {children.Count} boxes.");
  }

  private static void _ParseItemLocations(
    byte[] bytes,
    Box box,
    Dictionary<uint, ItemLocation> locations
  ) {
    var data = bytes.AsSpan(box.PayloadStart, box.PayloadLength);
    if (data.Length < 8)
      throw new InvalidDataException("HEIF: iloc is truncated.");

    var version = data[0];
    if (version > 2)
      throw new NotSupportedException($"HEIF: iloc version {version} is not defined by the implemented syntax.");

    var at = 4;

    var sizes = _ReadByte(data, ref at);
    var offsetSize = sizes >> 4;
    var lengthSize = sizes & 0x0F;

    var sizes2 = _ReadByte(data, ref at);
    var baseOffsetSize = sizes2 >> 4;
    var indexSize = version is 1 or 2 ? sizes2 & 0x0F : 0;

    var count = version < 2 ? (uint)_ReadU16(data, ref at) : _ReadU32(data, ref at);

    for (uint i = 0; i < count; ++i) {
      var itemId = version < 2 ? (uint)_ReadU16(data, ref at) : _ReadU32(data, ref at);
      var constructionMethod = 0;

      if (version is 1 or 2)
        constructionMethod = _ReadU16(data, ref at) & 0x0FFF;

      var dataReferenceIndex = _ReadU16(data, ref at);
      var baseOffset = _ReadUIntN(data, ref at, baseOffsetSize);
      var extentCount = _ReadU16(data, ref at);
      var extents = new ItemExtent[extentCount];

      for (var e = 0; e < extentCount; ++e) {
        if (version is 1 or 2 && indexSize > 0)
          _ = _ReadUIntN(data, ref at, indexSize);

        var offset = _ReadUIntN(data, ref at, offsetSize);
        var length = _ReadUIntN(data, ref at, lengthSize);
        extents[e] = new(offset, length);
      }

      locations[itemId] = new(itemId, constructionMethod, dataReferenceIndex, baseOffset, extents);
    }
  }

  private static void _ParseItemProperties(
    byte[] bytes,
    Box iprp,
    List<PropertyBox> properties,
    Dictionary<uint, List<PropertyAssociation>> associations
  ) {
    foreach (var child in _ReadBoxes(bytes, iprp.PayloadStart, iprp.End)) {
      if (child.Type == IsoBmffBox.Ipco) {
        foreach (var property in _ReadBoxes(bytes, child.PayloadStart, child.End))
          properties.Add(new(
            property.Type,
            bytes.AsSpan(property.PayloadStart, property.PayloadLength).ToArray()));
        continue;
      }

      if (child.Type != IsoBmffBox.Ipma)
        continue;

      var data = bytes.AsSpan(child.PayloadStart, child.PayloadLength);
      if (data.Length < 8)
        throw new InvalidDataException("HEIF: ipma is truncated.");

      var version = data[0];
      var flags = (data[1] << 16) | (data[2] << 8) | data[3];
      var wideIndex = (flags & 1) != 0;
      var at = 4;
      var entryCount = _ReadU32(data, ref at);

      for (uint entry = 0; entry < entryCount; ++entry) {
        var itemId = version < 1 ? (uint)_ReadU16(data, ref at) : _ReadU32(data, ref at);
        var associationCount = _ReadByte(data, ref at);
        if (!associations.TryGetValue(itemId, out var list))
          associations[itemId] = list = [];

        for (var i = 0; i < associationCount; ++i) {
          int propertyIndex;
          bool essential;

          if (wideIndex) {
            var raw = _ReadU16(data, ref at);
            essential = (raw & 0x8000) != 0;
            propertyIndex = raw & 0x7FFF;
          } else {
            var raw = _ReadByte(data, ref at);
            essential = (raw & 0x80) != 0;
            propertyIndex = raw & 0x7F;
          }

          if (propertyIndex != 0)
            list.Add(new(propertyIndex, essential));
        }
      }
    }
  }

  private static void _ParseItemReferences(byte[] bytes, Box iref, HashSet<uint> hidden) {
    var payload = bytes.AsSpan(iref.PayloadStart, iref.PayloadLength);
    if (payload.Length < 4)
      throw new InvalidDataException("HEIF: iref is truncated.");

    var version = payload[0];
    foreach (var reference in _ReadBoxes(bytes, iref.PayloadStart + 4, iref.End)) {
      if (reference.Type is not ("thmb" or "auxl"))
        continue;

      var data = bytes.AsSpan(reference.PayloadStart, reference.PayloadLength);
      var at = 0;
      var fromId = version == 0 ? (uint)_ReadU16(data, ref at) : _ReadU32(data, ref at);
      hidden.Add(fromId);

      var count = _ReadU16(data, ref at);
      for (var i = 0; i < count; ++i)
        _ = version == 0 ? (uint)_ReadU16(data, ref at) : _ReadU32(data, ref at);
    }
  }

  private static List<Box> _ReadBoxes(byte[] bytes, int start, int end) {
    if (start < 0 || end < start || end > bytes.Length)
      throw new InvalidDataException("ISOBMFF box range leaves the file.");

    var boxes = new List<Box>();
    var at = start;

    while (at < end) {
      if (end - at < 8)
        throw new InvalidDataException($"ISOBMFF: {end - at} trailing byte(s) cannot form a box header.");

      var size32 = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));
      var type = Encoding.ASCII.GetString(bytes, at + 4, 4);
      var header = 8;
      ulong size;

      if (size32 == 0) {
        size = (ulong)(end - at);
      } else if (size32 == 1) {
        if (end - at < 16)
          throw new InvalidDataException($"ISOBMFF: extended-size box '{type}' is truncated.");
        size = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(at + 8));
        header = 16;
      } else {
        size = size32;
      }

      if (size < (ulong)header || size > (ulong)(end - at) || size > int.MaxValue)
        throw new InvalidDataException($"ISOBMFF: box '{type}' states invalid size {size} at offset {at}.");

      var intSize = (int)size;
      boxes.Add(new(type, at, header, intSize));
      at += intSize;

      if (size32 == 0)
        break;
    }

    return boxes;
  }

  private static CleanAperture _ReadCleanAperture(byte[] data) => new(
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)),
    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20)),
    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(24)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(28))
  );

  private static bool _TryResolveCleanAperture(
    CleanAperture clap,
    int codedWidth,
    int codedHeight,
    out int cropX,
    out int cropY,
    out int cropWidth,
    out int cropHeight
  ) {
    cropX = cropY = cropWidth = cropHeight = 0;

    if (clap.WidthD == 0 || clap.HeightD == 0 || clap.HorizOffD == 0 || clap.VertOffD == 0)
      return false;
    if (codedWidth <= 0 || codedHeight <= 0)
      return false;

    var cleanWidth = (2L * clap.WidthN + clap.WidthD) / (2L * clap.WidthD);
    var cleanHeight = (2L * clap.HeightN + clap.HeightD) / (2L * clap.HeightD);

    if (cleanWidth <= 0 || cleanHeight <= 0 || cleanWidth > codedWidth || cleanHeight > codedHeight)
      return false;
    if (cleanWidth * cleanHeight * 3 > int.MaxValue)
      return false;

    if (cleanWidth == codedWidth && cleanHeight == codedHeight)
      return false;

    var left = _FloorDiv(
      (codedWidth - cleanWidth) * clap.HorizOffD + 2L * clap.HorizOffN,
      2L * clap.HorizOffD);
    var top = _FloorDiv(
      (codedHeight - cleanHeight) * clap.VertOffD + 2L * clap.VertOffN,
      2L * clap.VertOffD);

    if (left < 0 || top < 0 || left + cleanWidth > codedWidth || top + cleanHeight > codedHeight)
      return false;

    cropX = checked((int)left);
    cropY = checked((int)top);
    cropWidth = checked((int)cleanWidth);
    cropHeight = checked((int)cleanHeight);
    return true;
  }

  private static byte[] _CropRgb24(
    byte[] source,
    int sourceWidth,
    int x,
    int y,
    int width,
    int height
  ) {
    var sourceStride = checked(sourceWidth * 3);
    var targetStride = checked(width * 3);
    var result = new byte[checked(targetStride * height)];

    for (var row = 0; row < height; ++row) {
      var sourceOffset = checked((y + row) * sourceStride + x * 3);
      source.AsSpan(sourceOffset, targetStride).CopyTo(result.AsSpan(row * targetStride, targetStride));
    }

    return result;
  }

  private static long _FloorDiv(long numerator, long denominator) {
    var quotient = numerator / denominator;
    var remainder = numerator % denominator;
    if (remainder != 0 && numerator < 0)
      --quotient;
    return quotient;
  }

  private static byte _ReadByte(ReadOnlySpan<byte> data, ref int at) {
    if ((uint)at >= (uint)data.Length)
      throw new InvalidDataException("HEIF: box payload ends unexpectedly.");
    return data[at++];
  }

  private static ushort _ReadU16(ReadOnlySpan<byte> data, ref int at) {
    if (at < 0 || at + 2 > data.Length)
      throw new InvalidDataException("HEIF: box payload ends in a 16-bit field.");
    var value = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
    at += 2;
    return value;
  }

  private static uint _ReadU32(ReadOnlySpan<byte> data, ref int at) {
    if (at < 0 || at + 4 > data.Length)
      throw new InvalidDataException("HEIF: box payload ends in a 32-bit field.");
    var value = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
    at += 4;
    return value;
  }

  private static string _ReadFourCc(ReadOnlySpan<byte> data, ref int at) {
    if (at < 0 || at + 4 > data.Length)
      throw new InvalidDataException("HEIF: box payload ends in a four-character code.");
    var value = Encoding.ASCII.GetString(data.Slice(at, 4));
    at += 4;
    return value;
  }

  private static ulong _ReadUIntN(ReadOnlySpan<byte> data, ref int at, int bytes) {
    if (bytes is < 0 or > 8)
      throw new InvalidDataException($"HEIF: iloc uses unsupported integer width {bytes}.");
    if (at < 0 || at + bytes > data.Length)
      throw new InvalidDataException("HEIF: iloc ends in a variable-width integer.");

    ulong value = 0;
    for (var i = 0; i < bytes; ++i)
      value = (value << 8) | data[at++];

    return value;
  }

  private readonly record struct Box(string Type, int Start, int HeaderSize, int Size) {
    internal int PayloadStart => this.Start + this.HeaderSize;
    internal int PayloadLength => this.Size - this.HeaderSize;
    internal int End => this.Start + this.Size;
  }

  private readonly record struct PropertyBox(string Type, byte[] Data);
  private readonly record struct PropertyAssociation(int PropertyIndex, bool Essential);
  private readonly record struct ItemExtent(ulong Offset, ulong Length);
  private readonly record struct ItemLocation(
    uint ItemId,
    int ConstructionMethod,
    ushort DataReferenceIndex,
    ulong BaseOffset,
    ItemExtent[] Extents
  );

  private readonly record struct CleanAperture(
    uint WidthN,
    uint WidthD,
    uint HeightN,
    uint HeightD,
    int HorizOffN,
    uint HorizOffD,
    int VertOffN,
    uint VertOffD
  );

  private readonly record struct ItemDescriptor(
    string ItemType,
    int CodedWidth,
    int CodedHeight,
    CleanAperture? Aperture,
    byte[]? HevcConfiguration
  );

  private readonly record struct LegacyDescriptor(int Width, int Height, CleanAperture? Aperture);

  private sealed record HeifContainer(
    string Brand,
    uint PrimaryItemId,
    Dictionary<uint, string> ItemInfos,
    Dictionary<uint, ItemLocation> Locations,
    List<PropertyBox> Properties,
    Dictionary<uint, List<PropertyAssociation>> Associations,
    HashSet<uint> HiddenImageItems,
    List<Box> TopLevelBoxes,
    Box? IdatBox
  ) {
    internal IReadOnlyDictionary<uint, string> Items => this.ItemInfos;
  }
}
