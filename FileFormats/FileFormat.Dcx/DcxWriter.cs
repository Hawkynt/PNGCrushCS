using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Pcx;

namespace FileFormat.Dcx;

/// <summary>Assembles DCX (multi-page PCX) file bytes from a DcxFile data model.</summary>
public static class DcxWriter {

  private const uint _MAGIC = 0x3ADE68B1;

  public static byte[] ToBytes(DcxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pageCount = file.Pages.Count;

    // Serialize each page to PCX bytes
    var pageBytes = new List<byte[]>(pageCount);
    foreach (var page in file.Pages)
      pageBytes.Add(PcxWriter.ToBytes(page));

    // Page table: magic (4) + offsets (pageCount * 4) + zero terminator (4)
    var headerSize = 4 + (pageCount + 1) * 4;

    // Calculate offsets for each page
    var offsets = new uint[pageCount];
    var currentOffset = (uint)headerSize;
    for (var i = 0; i < pageCount; ++i) {
      offsets[i] = currentOffset;
      currentOffset += (uint)pageBytes[i].Length;
    }

    // Build the output
    using var ms = new MemoryStream();
    var buffer = new byte[4];

    // Write magic
    BinaryPrimitives.WriteUInt32LittleEndian(buffer, _MAGIC);
    ms.Write(buffer, 0, 4);

    // Write page offsets
    for (var i = 0; i < pageCount; ++i) {
      BinaryPrimitives.WriteUInt32LittleEndian(buffer, offsets[i]);
      ms.Write(buffer, 0, 4);
    }

    // Write zero terminator
    BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0);
    ms.Write(buffer, 0, 4);

    // Write page data
    foreach (var page in pageBytes)
      ms.Write(page, 0, page.Length);

    return ms.ToArray();
  }
}
