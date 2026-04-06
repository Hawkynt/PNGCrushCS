using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Mpo;

/// <summary>Reads MPO (Multi-Picture Object) files from bytes, streams, or file paths.</summary>
public static class MpoReader {

  private const int _MIN_SIZE = 4; // FF D8 FF E0/E1/E2 minimum
  private const byte _MARKER_PREFIX = 0xFF;
  private const byte _SOI = 0xD8;
  private const byte _APP2 = 0xE2;
  private const byte _SOS = 0xDA;

  /// <summary>The MPF identifier bytes: "MPF\0".</summary>
  internal static readonly byte[] MpfIdentifier = [(byte)'M', (byte)'P', (byte)'F', 0x00];

  public static MpoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MPO file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MpoFile FromStream(Stream stream) {
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

  public static MpoFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MpoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException("Data too small for a valid MPO file.");

    if (data[0] != _MARKER_PREFIX || data[1] != _SOI)
      throw new InvalidDataException("Invalid JPEG/MPO signature.");

    // Try to parse MP Entry offsets from the APP2 MPF marker
    var mpEntryOffsets = _TryParseMpEntries(data);
    if (mpEntryOffsets != null && mpEntryOffsets.Count > 0)
      return _ExtractFromMpEntries(data, mpEntryOffsets);

    // Fallback: scan for JPEG SOI markers to find image boundaries
    return _ExtractBySoiScan(data);
  }

  /// <summary>Tries to find and parse the APP2 MPF marker in the first JPEG image.</summary>
  private static List<MpEntry>? _TryParseMpEntries(byte[] data) {
    var pos = 2; // skip SOI

    while (pos + 2 <= data.Length) {
      if (data[pos] != _MARKER_PREFIX)
        return null;

      var marker = data[pos + 1];

      // If we hit SOS, stop scanning markers (compressed data follows)
      if (marker == _SOS)
        return null;

      // Skip padding FF bytes
      if (marker == _MARKER_PREFIX) {
        ++pos;
        continue;
      }

      // Skip non-segment markers (RST, SOI, EOI, TEM)
      if (marker is 0x00 or (>= 0xD0 and <= 0xD7) or 0xD9 or 0x01) {
        pos += 2;
        continue;
      }

      if (pos + 4 > data.Length)
        return null;

      var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2));
      var segmentDataStart = pos + 4;
      var segmentDataLength = segmentLength - 2;

      if (marker == _APP2 && segmentDataLength >= 4) {
        // Check for "MPF\0" identifier
        if (segmentDataStart + 4 <= data.Length &&
            data[segmentDataStart] == MpfIdentifier[0] &&
            data[segmentDataStart + 1] == MpfIdentifier[1] &&
            data[segmentDataStart + 2] == MpfIdentifier[2] &&
            data[segmentDataStart + 3] == MpfIdentifier[3]) {
          var mpHeaderStart = segmentDataStart + 4;
          return _ParseMpIndex(data, mpHeaderStart);
        }
      }

      pos += 2 + segmentLength;
    }

    return null;
  }

  /// <summary>Parses the MP Header and MP Index IFD from the given position.</summary>
  private static List<MpEntry>? _ParseMpIndex(byte[] data, int mpHeaderStart) {
    if (mpHeaderStart + 8 > data.Length)
      return null;

    // Byte order: "II" (0x4949) = little-endian, "MM" (0x4D4D) = big-endian
    var byteOrder = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(mpHeaderStart));
    var isLittleEndian = byteOrder == 0x4949;
    if (!isLittleEndian && byteOrder != 0x4D4D)
      return null;

    // Verify TIFF-like marker (0x002A)
    var tiffMarker = isLittleEndian
      ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(mpHeaderStart + 2))
      : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(mpHeaderStart + 2));
    if (tiffMarker != 0x002A)
      return null;

    // Offset to first IFD (relative to mpHeaderStart)
    var ifdOffset = isLittleEndian
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(mpHeaderStart + 4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(mpHeaderStart + 4));

    var ifdPos = mpHeaderStart + (int)ifdOffset;
    if (ifdPos + 2 > data.Length)
      return null;

    // Read IFD entry count
    var entryCount = isLittleEndian
      ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ifdPos))
      : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(ifdPos));

    var numberOfImages = 0u;
    var mpEntryOffset = -1;

    var tagPos = ifdPos + 2;
    for (var i = 0; i < entryCount; ++i) {
      if (tagPos + 12 > data.Length)
        break;

      var tag = isLittleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(tagPos))
        : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tagPos));

      var count = isLittleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(tagPos + 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(tagPos + 4));

      var valueOrOffset = isLittleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(tagPos + 8))
        : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(tagPos + 8));

      switch (tag) {
        case 0xB001: // NumberOfImages
          numberOfImages = valueOrOffset;
          break;
        case 0xB002: // MPEntry
          mpEntryOffset = mpHeaderStart + (int)valueOrOffset;
          break;
      }

      tagPos += 12;
    }

    if (numberOfImages == 0 || mpEntryOffset < 0)
      return null;

    // Parse MP Entry array (16 bytes per entry)
    var entries = new List<MpEntry>((int)numberOfImages);
    for (var i = 0; i < (int)numberOfImages; ++i) {
      var entryPos = mpEntryOffset + i * 16;
      if (entryPos + 16 > data.Length)
        break;

      var attribute = isLittleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryPos))
        : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryPos));

      var imageSize = isLittleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryPos + 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryPos + 4));

      var imageOffset = isLittleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryPos + 8))
        : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryPos + 8));

      entries.Add(new(attribute, imageSize, imageOffset));
    }

    return entries;
  }

  /// <summary>Extracts JPEG images using MP Entry offset/size data.</summary>
  private static MpoFile _ExtractFromMpEntries(byte[] data, List<MpEntry> entries) {
    var images = new List<byte[]>(entries.Count);

    for (var i = 0; i < entries.Count; ++i) {
      var entry = entries[i];

      int start;
      int size;

      if (i == 0) {
        // First image: offset is 0, meaning start of file. Size from entry.
        start = 0;
        size = entry.ImageSize > 0 ? (int)entry.ImageSize : _FindFirstImageEnd(data);
      } else {
        start = (int)entry.ImageOffset;
        size = (int)entry.ImageSize;
      }

      if (start < 0 || start >= data.Length)
        continue;
      if (size <= 0 || start + size > data.Length)
        size = data.Length - start;

      var imageData = new byte[size];
      data.AsSpan(start, size).CopyTo(imageData.AsSpan(0));
      images.Add(imageData);
    }

    return new MpoFile { Images = images };
  }

  /// <summary>Finds the end of the first JPEG image by scanning for the next SOI marker.</summary>
  private static int _FindFirstImageEnd(byte[] data) {
    // Scan for the next FF D8 after the first one
    for (var i = 2; i < data.Length - 1; ++i)
      if (data[i] == _MARKER_PREFIX && data[i + 1] == _SOI)
        return i;

    return data.Length;
  }

  /// <summary>Extracts JPEG images by scanning for SOI (FF D8) markers.</summary>
  private static MpoFile _ExtractBySoiScan(byte[] data) {
    var soiPositions = new List<int>();

    for (var i = 0; i < data.Length - 1; ++i)
      if (data[i] == _MARKER_PREFIX && data[i + 1] == _SOI) {
        // Avoid false positives inside compressed data by checking for a valid marker after SOI
        if (i + 2 < data.Length && data[i + 2] == _MARKER_PREFIX)
          soiPositions.Add(i);
        else if (i == 0)
          soiPositions.Add(i); // First SOI at position 0 is always valid
      }

    if (soiPositions.Count == 0)
      throw new InvalidDataException("No valid JPEG images found in MPO data.");

    var images = new List<byte[]>(soiPositions.Count);
    for (var i = 0; i < soiPositions.Count; ++i) {
      var start = soiPositions[i];
      var end = i + 1 < soiPositions.Count ? soiPositions[i + 1] : data.Length;
      var size = end - start;

      var imageData = new byte[size];
      data.AsSpan(start, size).CopyTo(imageData.AsSpan(0));
      images.Add(imageData);
    }

    return new MpoFile { Images = images };
  }

  /// <summary>Internal MP Entry record for parsing.</summary>
  internal readonly record struct MpEntry(uint Attribute, uint ImageSize, uint ImageOffset);
}
