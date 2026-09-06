using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileFormat.Avif;

/// <summary>
/// The parts of an ISO base media <c>meta</c> box that say where an image item's bytes live. AVIF
/// stores the coded picture as an item rather than a track, so the payload has to be reassembled
/// from <c>iloc</c> extents before any codec sees it, and a file whose <c>mdat</c> happens to hold
/// exactly one item is a coincidence rather than a rule.
/// </summary>
internal sealed class AvifItemLayout {

  /// <summary>Item id of the primary item, from <c>pitm</c>.</summary>
  public uint PrimaryItemId { get; private set; } = 1;

  /// <summary>Item type four-character code per item id, from <c>iinf</c>.</summary>
  public Dictionary<uint, string> ItemTypes { get; } = [];

  /// <summary>Byte extents per item id, from <c>iloc</c>, already resolved against the base offset.</summary>
  public Dictionary<uint, List<(long Offset, long Length)>> ItemExtents { get; } = [];

  /// <summary>Items whose extents address <c>idat</c> rather than the file, per construction_method.</summary>
  public HashSet<uint> ItemsInItemData { get; } = [];

  /// <summary>Contents of the <c>idat</c> box, when the file has one.</summary>
  public byte[] ItemData { get; private set; } = [];

  /// <summary>Property indices associated with each item, from <c>ipma</c>, one-based.</summary>
  public Dictionary<uint, List<int>> ItemProperties { get; } = [];

  /// <summary>Properties in <c>ipco</c> order.</summary>
  public List<IsoBmffBox> Properties { get; } = [];

  /// <summary>Item references from <c>iref</c>, as (referenceType, fromItem, toItems).</summary>
  public List<(uint Type, uint From, uint[] To)> References { get; } = [];

  /// <summary>Primary item dimensions from its <c>ispe</c> property.</summary>
  public int Width { get; private set; }

  public int Height { get; private set; }

  public static AvifItemLayout Parse(byte[] file) {
    var boxes = IsoBmffBox.ReadBoxes(file, 0, file.Length);
    var meta = boxes.FirstOrDefault(b => b.Type == IsoBmffBox.Meta)
      ?? throw new InvalidDataException("AVIF: no meta box.");

    var layout = new AvifItemLayout();
    foreach (var child in IsoBmffBox.ReadBoxes(meta.Data, 4, meta.Data.Length - 4))
      if (child.Type == IsoBmffBox.Pitm)
        layout._ParsePrimaryItem(child.Data);
      else if (child.Type == IsoBmffBox.Iinf)
        layout._ParseItemInfo(child.Data);
      else if (child.Type == IsoBmffBox.Iloc)
        layout._ParseItemLocation(child.Data);
      else if (child.Type == IsoBmffBox.Iprp)
        layout._ParseItemProperties(child.Data);
      else if (child.Type == IsoBmffBox.Iref)
        layout._ParseItemReferences(child.Data);
      else if (child.Type == IsoBmffBox.Idat)
        layout.ItemData = child.Data;

    layout._ResolveDimensions();
    return layout;
  }

  /// <summary>Gathers one item's bytes, following its extents in order.</summary>
  public byte[] GetItemData(byte[] file, uint itemId) {
    if (!this.ItemExtents.TryGetValue(itemId, out var extents))
      throw new InvalidDataException($"AVIF: item {itemId} has no location.");

    var source = this.ItemsInItemData.Contains(itemId) ? this.ItemData : file;
    var total = extents.Sum(e => e.Length);
    if (total is <= 0 or > int.MaxValue)
      throw new InvalidDataException($"AVIF: item {itemId} has an unusable length of {total} bytes.");

    var result = new byte[total];
    var written = 0;
    foreach (var (offset, length) in extents) {
      if (offset < 0 || length < 0 || offset + length > source.Length)
        throw new InvalidDataException($"AVIF: item {itemId} points outside the file.");

      Array.Copy(source, (int)offset, result, written, (int)length);
      written += (int)length;
    }

    return result;
  }

  /// <summary>Returns a property of the given type attached to an item, if it has one.</summary>
  public byte[]? GetProperty(uint itemId, uint type) {
    if (!this.ItemProperties.TryGetValue(itemId, out var indices))
      return null;

    foreach (var index in indices)
      if (index >= 1 && index <= this.Properties.Count && this.Properties[index - 1].Type == type)
        return this.Properties[index - 1].Data;

    return null;
  }

  /// <summary>Finds the alpha auxiliary item attached to an image item, if the file carries one.</summary>
  public uint? FindAlphaItem(uint colorItemId) {
    foreach (var (type, from, to) in this.References) {
      if (type != IsoBmffBox.Auxl || !to.Contains(colorItemId))
        continue;

      var auxC = this.GetProperty(from, IsoBmffBox.AuxC);
      if (auxC == null)
        continue;

      // auxC carries a null-terminated URN; the alpha plane's is fixed by the AVIF specification.
      var urn = _ReadNullTerminated(auxC, 4);
      if (urn == "urn:mpeg:mpegB:cicp:systems:auxiliary:alpha")
        return from;
    }

    return null;
  }

  private void _ParsePrimaryItem(byte[] data) {
    if (data.Length < 6)
      return;

    this.PrimaryItemId = data[0] == 0
      ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
  }

  private void _ParseItemInfo(byte[] data) {
    var version = data[0];
    var offset = 4 + (version == 0 ? 2 : 4);

    foreach (var entry in IsoBmffBox.ReadBoxes(data, offset, data.Length - offset)) {
      if (entry.Type != IsoBmffBox.Infe || entry.Data.Length < 12)
        continue;

      var infeVersion = entry.Data[0];
      if (infeVersion < 2)
        continue;

      var p = 4;
      uint itemId;
      if (infeVersion == 2) {
        itemId = BinaryPrimitives.ReadUInt16BigEndian(entry.Data.AsSpan(p));
        p += 2;
      } else {
        itemId = BinaryPrimitives.ReadUInt32BigEndian(entry.Data.AsSpan(p));
        p += 4;
      }

      p += 2; // item_protection_index
      if (p + 4 > entry.Data.Length)
        continue;

      this.ItemTypes[itemId] = IsoBmffBox.FourCCToString(BinaryPrimitives.ReadUInt32BigEndian(entry.Data.AsSpan(p)));
    }
  }

  private void _ParseItemLocation(byte[] data) {
    var version = data[0];
    var offsetSize = data[4] >> 4;
    var lengthSize = data[4] & 0x0F;
    var baseOffsetSize = data[5] >> 4;
    var indexSize = version is 1 or 2 ? data[5] & 0x0F : 0;
    var p = 6;

    int itemCount;
    if (version < 2) {
      itemCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
      p += 2;
    } else {
      itemCount = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
      p += 4;
    }

    for (var i = 0; i < itemCount; ++i) {
      uint itemId;
      if (version < 2) {
        itemId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
        p += 2;
      } else {
        itemId = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
        p += 4;
      }

      var constructionMethod = 0;
      if (version is 1 or 2) {
        constructionMethod = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p)) & 0x0F;
        p += 2;
      }

      p += 2; // data_reference_index
      var baseOffset = _ReadValue(data, ref p, baseOffsetSize);
      var extentCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
      p += 2;

      var extents = new List<(long, long)>(extentCount);
      for (var e = 0; e < extentCount; ++e) {
        if (indexSize > 0)
          _ReadValue(data, ref p, indexSize);

        var extentOffset = _ReadValue(data, ref p, offsetSize);
        var extentLength = _ReadValue(data, ref p, lengthSize);
        extents.Add((baseOffset + extentOffset, extentLength));
      }

      this.ItemExtents[itemId] = extents;
      if (constructionMethod == 1)
        this.ItemsInItemData.Add(itemId);
      else if (constructionMethod == 2)
        throw new NotSupportedException("AVIF: item_reference extent construction is not supported.");
    }
  }

  private void _ParseItemProperties(byte[] data) {
    foreach (var child in IsoBmffBox.ReadBoxes(data, 0, data.Length))
      if (child.Type == IsoBmffBox.Ipco)
        this.Properties.AddRange(IsoBmffBox.ReadBoxes(child.Data, 0, child.Data.Length));
      else if (child.Type == IsoBmffBox.Ipma)
        this._ParseItemPropertyAssociation(child.Data);
  }

  private void _ParseItemPropertyAssociation(byte[] data) {
    var version = data[0];
    var flags = (data[1] << 16) | (data[2] << 8) | data[3];
    var p = 4;
    var entryCount = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
    p += 4;

    for (var i = 0; i < entryCount && p < data.Length; ++i) {
      uint itemId;
      if (version < 1) {
        itemId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
        p += 2;
      } else {
        itemId = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
        p += 4;
      }

      var associationCount = data[p++];
      var list = new List<int>(associationCount);
      for (var a = 0; a < associationCount; ++a)
        if ((flags & 1) != 0) {
          list.Add(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p)) & 0x7FFF);
          p += 2;
        } else
          list.Add(data[p++] & 0x7F);

      this.ItemProperties[itemId] = list;
    }
  }

  private void _ParseItemReferences(byte[] data) {
    var version = data[0];
    foreach (var reference in IsoBmffBox.ReadBoxes(data, 4, data.Length - 4)) {
      var payload = reference.Data;
      var p = 0;

      uint from;
      if (version == 0) {
        from = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(p));
        p += 2;
      } else {
        from = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(p));
        p += 4;
      }

      var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(p));
      p += 2;

      var to = new uint[count];
      for (var i = 0; i < count; ++i)
        if (version == 0) {
          to[i] = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(p));
          p += 2;
        } else {
          to[i] = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(p));
          p += 4;
        }

      this.References.Add((reference.Type, from, to));
    }
  }

  private void _ResolveDimensions() {
    var ispe = this.GetProperty(this.PrimaryItemId, IsoBmffBox.Ispe);
    if (ispe is not { Length: >= 12 })
      return;

    this.Width = (int)BinaryPrimitives.ReadUInt32BigEndian(ispe.AsSpan(4));
    this.Height = (int)BinaryPrimitives.ReadUInt32BigEndian(ispe.AsSpan(8));
  }

  private static long _ReadValue(byte[] data, ref int offset, int size) {
    var value = 0L;
    for (var i = 0; i < size; ++i)
      value = (value << 8) | data[offset + i];
    offset += size;
    return value;
  }

  private static string _ReadNullTerminated(byte[] data, int offset) {
    var end = offset;
    while (end < data.Length && data[end] != 0)
      ++end;
    return System.Text.Encoding.ASCII.GetString(data, offset, end - offset);
  }
}
