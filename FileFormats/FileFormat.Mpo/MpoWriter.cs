using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Mpo;

/// <summary>Assembles MPO (Multi-Picture Object) file bytes from an MpoFile data model.</summary>
public static class MpoWriter {

  // MP Index IFD tag IDs
  private const ushort _TAG_MP_VERSION = 0xB000;
  private const ushort _TAG_NUMBER_OF_IMAGES = 0xB001;
  private const ushort _TAG_MP_ENTRY = 0xB002;

  // TIFF type constants
  private const ushort _TYPE_UNDEFINED = 7;
  private const ushort _TYPE_LONG = 4;

  // MP Entry size: 16 bytes per image
  private const int _MP_ENTRY_SIZE = 16;

  // First image attribute flag: representative image + JPEG type
  private const uint _FIRST_IMAGE_ATTRIBUTE = 0x030000; // dependent=0, representative=1, type=JPEG(01)

  // Subsequent image attribute flag: multi-frame panorama type
  private const uint _OTHER_IMAGE_ATTRIBUTE = 0x000000;

  public static byte[] ToBytes(MpoFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.Images.Count == 0)
      return [];

    if (file.Images.Count == 1)
      return _WriteSingleImage(file.Images[0]);

    return _WriteMultipleImages(file.Images);
  }

  /// <summary>Writes a single-image MPO by injecting an APP2 MPF marker into the JPEG.</summary>
  private static byte[] _WriteSingleImage(byte[] jpegData) {
    // Build a dummy MPF to know its size, then calculate actual first-image size
    var dummyMpf = _BuildMpfSegment([(0u, (uint)jpegData.Length)], isSingleImage: true);
    var injected = _InjectMpfSegment(jpegData, dummyMpf);
    var actualSize = (uint)injected.Length;

    // Rebuild with correct size
    var mpfSegment = _BuildMpfSegment([(0u, actualSize)], isSingleImage: true);
    return _InjectMpfSegment(jpegData, mpfSegment);
  }

  /// <summary>Writes a multi-image MPO by injecting APP2 MPF into the first image and concatenating all.</summary>
  private static byte[] _WriteMultipleImages(IReadOnlyList<byte[]> images) {
    // Phase 1: Build a preliminary MPF segment with dummy offsets to compute its size
    var dummyOffsets = new (uint offset, uint size)[images.Count];
    for (var i = 0; i < images.Count; ++i)
      dummyOffsets[i] = (0, (uint)images[i].Length);

    var dummyMpf = _BuildMpfSegment(dummyOffsets, isSingleImage: false);
    var firstImageWithMpf = _InjectMpfSegment(images[0], dummyMpf);
    var firstImageSize = (uint)firstImageWithMpf.Length;

    // Phase 2: Calculate actual offsets
    var offsets = new (uint offset, uint size)[images.Count];
    offsets[0] = (0, firstImageSize);
    var currentOffset = firstImageSize;
    for (var i = 1; i < images.Count; ++i) {
      offsets[i] = (currentOffset, (uint)images[i].Length);
      currentOffset += (uint)images[i].Length;
    }

    // Phase 3: Rebuild MPF segment with correct offsets and rebuild first image
    var mpfSegment = _BuildMpfSegment(offsets, isSingleImage: false);
    firstImageWithMpf = _InjectMpfSegment(images[0], mpfSegment);

    // Verify the size hasn't changed (it shouldn't since the segment size is fixed)
    if ((uint)firstImageWithMpf.Length != firstImageSize) {
      // Recalculate offsets if size changed
      firstImageSize = (uint)firstImageWithMpf.Length;
      offsets[0] = (0, firstImageSize);
      currentOffset = firstImageSize;
      for (var i = 1; i < images.Count; ++i) {
        offsets[i] = (currentOffset, (uint)images[i].Length);
        currentOffset += (uint)images[i].Length;
      }

      mpfSegment = _BuildMpfSegment(offsets, isSingleImage: false);
      firstImageWithMpf = _InjectMpfSegment(images[0], mpfSegment);
    }

    // Phase 4: Concatenate all images
    using var ms = new MemoryStream((int)currentOffset);
    ms.Write(firstImageWithMpf, 0, firstImageWithMpf.Length);
    for (var i = 1; i < images.Count; ++i)
      ms.Write(images[i], 0, images[i].Length);

    return ms.ToArray();
  }

  /// <summary>Builds the APP2 MPF segment bytes (excluding the FF E2 marker and length).</summary>
  private static byte[] _BuildMpfSegment(IReadOnlyList<(uint offset, uint size)> entries, bool isSingleImage) {
    // MP Header: byte order (4) + IFD offset (4) = 8 bytes
    // MP Index IFD: entry count (2) + 3 IFD entries (3*12=36) + next IFD offset (4) = 42 bytes
    // MP Entry data: entries.Count * 16 bytes
    // MPFVersion data: 4 bytes ("0100")
    var mpEntryDataSize = entries.Count * _MP_ENTRY_SIZE;
    var ifdSize = 2 + 3 * 12 + 4; // 42 bytes
    var versionDataOffset = 8 + ifdSize; // offset from MP Header start
    var mpEntryDataOffset = versionDataOffset + 4; // after version data
    var totalSize = 8 + ifdSize + 4 + mpEntryDataSize;

    var buffer = new byte[totalSize];
    var span = buffer.AsSpan();

    // MP Header: big-endian byte order "MM" + 0x002A + offset to first IFD (8)
    buffer[0] = (byte)'M';
    buffer[1] = (byte)'M';
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0x002A);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], 8); // IFD starts right after header

    // MP Index IFD
    var ifdPos = 8;
    BinaryPrimitives.WriteUInt16BigEndian(span[ifdPos..], 3); // 3 IFD entries

    // Tag 0xB000: MPFVersion = "0100"
    var tagPos = ifdPos + 2;
    BinaryPrimitives.WriteUInt16BigEndian(span[tagPos..], _TAG_MP_VERSION);
    BinaryPrimitives.WriteUInt16BigEndian(span[(tagPos + 2)..], _TYPE_UNDEFINED);
    BinaryPrimitives.WriteUInt32BigEndian(span[(tagPos + 4)..], 4);
    BinaryPrimitives.WriteUInt32BigEndian(span[(tagPos + 8)..], (uint)versionDataOffset);

    // Tag 0xB001: NumberOfImages
    tagPos += 12;
    BinaryPrimitives.WriteUInt16BigEndian(span[tagPos..], _TAG_NUMBER_OF_IMAGES);
    BinaryPrimitives.WriteUInt16BigEndian(span[(tagPos + 2)..], _TYPE_LONG);
    BinaryPrimitives.WriteUInt32BigEndian(span[(tagPos + 4)..], 1);
    BinaryPrimitives.WriteUInt32BigEndian(span[(tagPos + 8)..], (uint)entries.Count);

    // Tag 0xB002: MPEntry
    tagPos += 12;
    BinaryPrimitives.WriteUInt16BigEndian(span[tagPos..], _TAG_MP_ENTRY);
    BinaryPrimitives.WriteUInt16BigEndian(span[(tagPos + 2)..], _TYPE_UNDEFINED);
    BinaryPrimitives.WriteUInt32BigEndian(span[(tagPos + 4)..], (uint)mpEntryDataSize);
    BinaryPrimitives.WriteUInt32BigEndian(span[(tagPos + 8)..], (uint)mpEntryDataOffset);

    // Next IFD offset (0 = no more IFDs)
    tagPos += 12;
    BinaryPrimitives.WriteUInt32BigEndian(span[tagPos..], 0);

    // MPFVersion value data: "0100"
    buffer[versionDataOffset] = (byte)'0';
    buffer[versionDataOffset + 1] = (byte)'1';
    buffer[versionDataOffset + 2] = (byte)'0';
    buffer[versionDataOffset + 3] = (byte)'0';

    // MP Entry data
    for (var i = 0; i < entries.Count; ++i) {
      var entryPos = mpEntryDataOffset + i * _MP_ENTRY_SIZE;
      var attribute = i == 0 ? _FIRST_IMAGE_ATTRIBUTE : _OTHER_IMAGE_ATTRIBUTE;

      BinaryPrimitives.WriteUInt32BigEndian(span[entryPos..], attribute);
      BinaryPrimitives.WriteUInt32BigEndian(span[(entryPos + 4)..], entries[i].size);
      BinaryPrimitives.WriteUInt32BigEndian(span[(entryPos + 8)..], entries[i].offset);
      // Dependent image entries (2 + 2 bytes) = 0
      BinaryPrimitives.WriteUInt16BigEndian(span[(entryPos + 12)..], 0);
      BinaryPrimitives.WriteUInt16BigEndian(span[(entryPos + 14)..], 0);
    }

    return buffer;
  }

  /// <summary>Injects the MPF APP2 segment into a JPEG byte array after the SOI marker.</summary>
  private static byte[] _InjectMpfSegment(byte[] jpegData, byte[] mpfPayload) {
    if (jpegData.Length < 2)
      throw new ArgumentException("JPEG data too small.", nameof(jpegData));

    // Find the insertion point: after SOI (first 2 bytes)
    // We want to insert after the SOI but before the first existing marker
    var insertionPoint = _FindMpfInsertionPoint(jpegData);

    // Build APP2 marker segment: FF E2 + length (2) + "MPF\0" (4) + mpfPayload
    var segmentDataLength = 2 + 4 + mpfPayload.Length; // length field + "MPF\0" + payload
    var fullSegmentSize = 2 + segmentDataLength; // FF E2 + length + data

    var result = new byte[jpegData.Length + fullSegmentSize];

    // Copy everything before insertion point
    jpegData.AsSpan(0, insertionPoint).CopyTo(result.AsSpan(0));

    // Write APP2 marker
    var pos = insertionPoint;
    result[pos] = 0xFF;
    result[pos + 1] = 0xE2;

    // Write segment length (includes itself but not the FF E2 marker bytes)
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(pos + 2), (ushort)(4 + mpfPayload.Length + 2));

    // Write "MPF\0"
    result[pos + 4] = (byte)'M';
    result[pos + 5] = (byte)'P';
    result[pos + 6] = (byte)'F';
    result[pos + 7] = 0x00;

    // Write MP payload
    mpfPayload.AsSpan(0, mpfPayload.Length).CopyTo(result.AsSpan(pos + 8));

    // Copy remainder of JPEG after insertion point
    jpegData.AsSpan(insertionPoint, jpegData.Length - insertionPoint).CopyTo(result.AsSpan(insertionPoint + fullSegmentSize));

    return result;
  }

  /// <summary>Finds the insertion point for the MPF segment. Inserts right after the SOI marker.</summary>
  private static int _FindMpfInsertionPoint(byte[] jpegData) => 2; // Right after FF D8
}
