using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

/// <summary>
/// Handles parsing PNG file structure.
/// </summary>
internal static class PngParser {
  public static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  public static List<PngChunk> ReadPngChunks(byte[] pngData) {
    ArgumentNullException.ThrowIfNull(pngData);

    var SIGNATURE_SIZE = PngSignature.Length;
    if (pngData.Length < SIGNATURE_SIZE || !pngData.Take(SIGNATURE_SIZE).SequenceEqual(PngSignature)) {
      throw new ArgumentException("Invalid PNG signature.");
    }

    List<PngChunk> chunks = [];
    
    using MemoryStream ms = new(pngData);
    ms.Position = SIGNATURE_SIZE; // Skip signature
    using BinaryReader reader = new(ms, Encoding.ASCII); // Specify ASCII for type reading

    while (ms.Position < ms.Length) {
      // Read chunk header
      var length = ReadUInt32BigEndian(reader);
      var typeBytes = reader.ReadBytes(4);
      var type = Encoding.ASCII.GetString(typeBytes);

      // Read chunk data
      byte[] data = [];
      switch (length) {
        case > 0: {
          // Protect against excessively large chunks potentially indicating corruption
          if (length > ms.Length - ms.Position - 4) // Length + Type + Data + CRC
            throw new ArgumentException($"Invalid chunk length for {type}. Declared {length}, remaining {ms.Length - ms.Position - 4}");

          data = reader.ReadBytes((int)length); // Cast is safe after check if length isn't huge
          break;
        }
      }

      // Read CRC
      var crc = ReadUInt32BigEndian(reader);

      // Verify CRC
      var calculatedCrc = CalculateCrc(typeBytes, data);
      if (crc != calculatedCrc) {
        // Allow specific chunks like gAMA to sometimes have incorrect CRCs in the wild, though strictly invalid
        if (type != "gAMA" && type != "cHRM") {
          Console.WriteLine($"Warning: CRC mismatch for chunk {type}. Expected {calculatedCrc:X8}, got {crc:X8}.");
          // Decide whether to throw or continue based on strictness
          // throw new ArgumentException($"CRC mismatch for chunk {type}. Expected {calculatedCrc:X8}, got {crc:X8}.");
        } else {
          Console.WriteLine($"Warning: Tolerating potential CRC mismatch for ancillary chunk {type}. Expected {calculatedCrc:X8}, got {crc:X8}.");
        }
      }


      chunks.Add(new PngChunk(length, type, data, crc)); // Store the read CRC

      if (type == "IEND") {
        break; // End of PNG data
      }
    }

    if (chunks.All(c => c.Type != "IHDR") || chunks.All(c => c.Type != "IDAT") || chunks.Last().Type != "IEND") {
      throw new ArgumentException("PNG is missing critical chunks (IHDR, IDAT, IEND) or IEND is not last.");
    }

    return chunks;
  }

  public static IhdrData ParseIhdr(PngChunk ihdrChunk) {
    if (ihdrChunk.Type != "IHDR" || ihdrChunk.Length != 13) {
      throw new ArgumentException("Invalid IHDR chunk provided.");
    }

    using MemoryStream ms = new(ihdrChunk.Data);
    using BinaryReader reader = new(ms);

    var width = ReadInt32BigEndian(reader);
    var height = ReadInt32BigEndian(reader);
    var bitDepth = reader.ReadByte();
    var colorType = reader.ReadByte();
    var compressionMethod = reader.ReadByte();
    var filterMethod = reader.ReadByte();
    var interlaceMethod = reader.ReadByte();

    if (compressionMethod != 0)
      throw new ArgumentException("Invalid compression method in IHDR (must be 0).");
    if (filterMethod != 0)
      throw new ArgumentException("Invalid filter method in IHDR (must be 0).");
    if (width <= 0 || height <= 0)
      throw new ArgumentException($"Invalid dimensions in IHDR ({width}x{height}).");

    // Basic validation for color type and bit depth combinations
    var validCombination = (colorType, bitDepth) switch {
      (0, 1 or 2 or 4 or 8 or 16) => true, // Grayscale
      (2, 8 or 16) => true,                // Truecolor
      (3, 1 or 2 or 4 or 8) => true,       // Indexed-color
      (4, 8 or 16) => true,                // Grayscale with alpha
      (6, 8 or 16) => true,                // Truecolor with alpha
      _ => false
    };
    switch (validCombination) {
      case false:
        Console.WriteLine($"Warning: Unusual ColorType ({colorType}) / BitDepth ({bitDepth}) combination in IHDR.");
        break;
    }
    // For a stricter tool: throw new ArgumentException($"Invalid ColorType ({colorType}) / BitDepth ({bitDepth}) combination in IHDR.");


    return new IhdrData(width, height, bitDepth, colorType, compressionMethod, filterMethod, interlaceMethod);
  }

  public static byte[] GetCombinedIdatData(List<PngChunk> chunks) {
    // Find the first and last IDAT chunks to handle potential interleaving with other chunks (allowed by spec but uncommon)
    var firstIdatIndex = chunks.FindIndex(c => c.Type == "IDAT");
    var lastIdatIndex = chunks.FindLastIndex(c => c.Type == "IDAT");

    switch (firstIdatIndex) {
      case -1:
        throw new ArgumentException("No IDAT chunks found.");
    }

    // Select only the IDAT chunks within the contiguous block (most common case)
    // Or simply concatenate all IDAT chunks regardless of position (more robust to weird files)
    var idatChunks = chunks.Where(c => c.Type == "IDAT");

    // Concatenate data from all found IDAT chunks
    using MemoryStream combinedStream = new();
    foreach (var idatChunk in idatChunks) {
      combinedStream.Write(idatChunk.Data, 0, idatChunk.Data.Length);
    }
    return combinedStream.ToArray();
  }


  // --- Endianness Helpers ---
  public static uint ReadUInt32BigEndian(BinaryReader reader) {
    return BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
  }

  public static int ReadInt32BigEndian(BinaryReader reader) {
    return BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
  }

  public static void WriteUInt32BigEndian(BinaryWriter writer, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    writer.Write(buffer);
  }

  public static uint CalculateCrc(byte[] type, byte[] data) {
    // CRC includes the chunk type bytes and the chunk data bytes
    Span<byte> bytesToCrc = new byte[type.Length + data.Length];
    type.CopyTo(bytesToCrc);
    data.CopyTo(bytesToCrc[type.Length..]);

    return Crc32.HashToUInt32(bytesToCrc); // System.IO.Hashing.Crc32
  }
}
