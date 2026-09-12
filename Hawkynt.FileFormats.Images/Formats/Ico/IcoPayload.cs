using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Ico;

/// <summary>
/// Converts between an icon entry's payload and the standalone image file that holds the same
/// picture.
/// </summary>
/// <remarks>
/// An entry's payload is one of two things: a whole PNG file, which needs no conversion at all, or
/// a bitmap that is deliberately not a BMP file. The bitmap is missing the fourteen-byte file
/// header every BMP begins with, states twice the height the picture actually has, and carries a
/// one-bit-per-pixel mask after the colours that the stated height is covering.
/// <para/>
/// Both directions of that conversion are needed by anything that wants to hand an entry to a
/// viewer, or build an entry out of something a viewer produced, and neither is a decode: no pixel
/// is examined, only the headers around them. Keeping the pair here means the two directions stay
/// each other's inverse, which is the property that breaks first when they are written twice.
/// </remarks>
public static class IcoPayload {

  /// <summary>The eight bytes every PNG file begins with.</summary>
  private static ReadOnlySpan<byte> _PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>The BITMAPFILEHEADER a real BMP begins with and an entry's bitmap does not.</summary>
  private const int _FileHeaderSize = 14;

  /// <summary>The oldest information header, which states its sizes in sixteen bits.</summary>
  private const int _CoreHeaderSize = 12;

  /// <summary>Whether the payload is a PNG file.</summary>
  /// <remarks>
  /// All eight signature bytes are checked rather than the four that name the format. The four
  /// remaining ones are there to catch a file mangled in transit — a line ending rewritten, a
  /// high bit stripped — and a payload that fails them is not something to hand a PNG decoder.
  /// </remarks>
  public static bool IsPng(ReadOnlySpan<byte> data)
    => data.Length >= _PngSignature.Length && data[.._PngSignature.Length].SequenceEqual(_PngSignature);

  /// <summary>Whether the data is a standalone BMP file rather than an entry's bare bitmap.</summary>
  public static bool IsBmpFile(ReadOnlySpan<byte> data)
    => data.Length >= _FileHeaderSize && data[0] == (byte)'B' && data[1] == (byte)'M';

  /// <summary>Reads the size and depth a PNG states in its image header.</summary>
  /// <exception cref="InvalidDataException">
  /// The data is too short to hold an image header, or does not open with one.
  /// </exception>
  public static (int Width, int Height, int BitsPerPixel) ReadPngHeader(ReadOnlySpan<byte> png) {
    // Signature, then the first chunk: four bytes of length, four naming it, thirteen of content.
    if (png.Length < 8 + 8 + 13)
      throw new InvalidDataException("PNG too small to contain an IHDR chunk.");

    // IHDR is required to be the first chunk, and the width is read from where it puts it. A file
    // opening with anything else is not a PNG this can measure, and reading the offsets anyway
    // yields a plausible-looking size taken from somebody else's chunk.
    if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
      throw new InvalidDataException("PNG's first chunk is not IHDR.");

    var width = BinaryPrimitives.ReadInt32BigEndian(png[16..]);
    var height = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
    var bitDepth = png[24];
    var colourType = png[25];

    var samplesPerPixel = colourType switch {
      0 => 1, // Grey
      2 => 3, // Colour
      3 => 1, // An index into a palette
      4 => 2, // Grey and alpha
      6 => 4, // Colour and alpha
      _ => 1
    };

    return (width, height, bitDepth * samplesPerPixel);
  }

  /// <summary>
  /// Wraps an entry's bitmap in the file header that makes it a BMP a viewer will open.
  /// </summary>
  /// <param name="iconDib">The entry's payload, an information header followed by colours and mask.</param>
  /// <param name="width">The picture's width, which the mask's stride is computed from.</param>
  /// <param name="height">The picture's real height, not the doubled one the header states.</param>
  /// <param name="bitsPerPixel">The depth, which decides how large a palette precedes the colours.</param>
  /// <remarks>
  /// Three things have to change and no others. A file header is put in front so the data names
  /// itself; the mask is dropped, because a BMP has no field that says what it is and a viewer
  /// reading the stated height would draw it as a second picture below the first; and the stated
  /// height is halved to the picture's own, since the doubling only ever described the two of them
  /// together.
  /// </remarks>
  /// <exception cref="InvalidDataException">The bitmap is too small, or states an impossible header size.</exception>
  public static byte[] IconDibToBmpFile(ReadOnlySpan<byte> iconDib, int width, int height, int bitsPerPixel) {
    if (iconDib.Length < 4)
      throw new InvalidDataException("Icon bitmap too small to state its header size.");

    var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(iconDib);
    if (headerSize < _CoreHeaderSize || headerSize > (uint)iconDib.Length)
      throw new InvalidDataException($"Icon bitmap states an impossible header size of {headerSize}.");

    // Below nine bits a colour is an index, and the palette sits between the header and the
    // colours. The header may name a shorter palette than the depth allows, and when it does that
    // is the one that is there.
    var paletteEntries = bitsPerPixel <= 8 ? 1 << bitsPerPixel : 0;
    if (bitsPerPixel <= 8 && headerSize >= 36) {
      var statedEntries = (int)BinaryPrimitives.ReadUInt32LittleEndian(iconDib[32..]);
      if (statedEntries > 0)
        paletteEntries = statedEntries;
    }

    var coloursAt = (int)headerSize + paletteEntries * 4;
    var colourBytes = (width * bitsPerPixel + 31) / 32 * 4 * height;

    // A truncated entry is kept rather than refused: everything up to where it stops is a picture,
    // just a short one, and a viewer shown it draws what arrived. Refusing loses that.
    if (coloursAt + colourBytes > iconDib.Length)
      colourBytes = Math.Max(0, iconDib.Length - coloursAt);

    var keptLength = coloursAt + colourBytes;
    var bmp = new byte[_FileHeaderSize + keptLength];

    bmp[0] = (byte)'B';
    bmp[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)(_FileHeaderSize + coloursAt));

    iconDib[..keptLength].CopyTo(bmp.AsSpan(_FileHeaderSize));

    // The oldest header states its height in sixteen bits at a different place from every later
    // one, and writing thirty-two bits there overwrites the depth.
    if (headerSize == _CoreHeaderSize)
      BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(_FileHeaderSize + 6), (short)height);
    else
      BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(_FileHeaderSize + 8), height);

    return bmp;
  }

  /// <summary>
  /// Turns a standalone BMP file into the bitmap an icon entry carries.
  /// </summary>
  /// <remarks>
  /// The inverse of <see cref="IconDibToBmpFile"/>: the file header comes off, the stated height
  /// doubles, and a mask of the right stride is appended. The mask is left clear, which means every
  /// pixel is drawn — a BMP says nothing about which ones should not be, and the only honest answer
  /// to a question the input never answered is the one that shows the whole picture.
  /// </remarks>
  /// <exception cref="InvalidDataException">The file is truncated or states an impossible header size.</exception>
  /// <exception cref="NotSupportedException">The bitmap is stored top down.</exception>
  public static byte[] BmpFileToIconDib(ReadOnlySpan<byte> bmpFile, out int width, out int height, out int bitsPerPixel) {
    if (bmpFile.Length < _FileHeaderSize + _CoreHeaderSize)
      throw new InvalidDataException("BMP too small to hold a file header and an information header.");

    var coloursAt = (int)BinaryPrimitives.ReadUInt32LittleEndian(bmpFile.Slice(10, 4));
    var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bmpFile.Slice(_FileHeaderSize, 4));
    if (headerSize < _CoreHeaderSize)
      throw new InvalidDataException($"BMP states an impossible header size of {headerSize}.");
    if (coloursAt < _FileHeaderSize + headerSize || coloursAt > bmpFile.Length)
      throw new InvalidDataException($"BMP points at its colours at {coloursAt}, which is outside the file.");

    if (headerSize == _CoreHeaderSize) {
      width = BinaryPrimitives.ReadInt16LittleEndian(bmpFile.Slice(_FileHeaderSize + 4, 2));
      height = BinaryPrimitives.ReadInt16LittleEndian(bmpFile.Slice(_FileHeaderSize + 6, 2));
      bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(bmpFile.Slice(_FileHeaderSize + 10, 2));
    } else {
      width = BinaryPrimitives.ReadInt32LittleEndian(bmpFile.Slice(_FileHeaderSize + 4, 4));
      height = BinaryPrimitives.ReadInt32LittleEndian(bmpFile.Slice(_FileHeaderSize + 8, 4));
      bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(bmpFile.Slice(_FileHeaderSize + 14, 2));
    }

    // A negative height means the rows are stored the other way up. An entry's bitmap has no way to
    // say so — the doubled height it states must be positive for the mask below it to make sense —
    // so the rows would have to be turned over, and that is a decode this deliberately is not.
    if (height < 0)
      throw new NotSupportedException("A top-down BMP cannot be stored in an icon entry without reordering its rows.");

    var headerAndPaletteLength = coloursAt - _FileHeaderSize;
    var colourLength = bmpFile.Length - coloursAt;
    var maskLength = (width + 31) / 32 * 4 * height;

    var dib = new byte[headerAndPaletteLength + colourLength + maskLength];
    bmpFile.Slice(_FileHeaderSize, headerAndPaletteLength).CopyTo(dib);
    bmpFile[coloursAt..].CopyTo(dib.AsSpan(headerAndPaletteLength));

    if (headerSize == _CoreHeaderSize)
      BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(6), (short)(height * 2));
    else
      BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2);

    // The stated byte count covers the mask as well, since the doubled height does.
    if (headerSize >= 24)
      BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), (uint)(colourLength + maskLength));

    return dib;
  }

  /// <summary>
  /// Turns a whole image file into an entry's payload, taking its size and depth from it.
  /// </summary>
  /// <remarks>
  /// A PNG becomes the payload unchanged, which is what an icon means by a PNG-bodied entry. A BMP
  /// is converted. Nothing else is accepted: an entry's payload is one of exactly those two things,
  /// and a file that is neither would have to be decoded and re-encoded first, which is a different
  /// operation with different failure modes and belongs to whoever wants it.
  /// </remarks>
  /// <exception cref="ArgumentException">The file is neither a PNG nor a BMP.</exception>
  public static (byte[] Payload, int Width, int Height, int BitsPerPixel, IcoImageFormat Format) Encode(ReadOnlySpan<byte> imageFile) {
    if (IsPng(imageFile)) {
      var (width, height, bitsPerPixel) = ReadPngHeader(imageFile);
      return (imageFile.ToArray(), width, height, bitsPerPixel, IcoImageFormat.Png);
    }

    if (IsBmpFile(imageFile)) {
      var payload = BmpFileToIconDib(imageFile, out var width, out var height, out var bitsPerPixel);
      return (payload, width, height, bitsPerPixel, IcoImageFormat.Bmp);
    }

    throw new ArgumentException("An icon entry holds a PNG or a BMP, and this is neither.", nameof(imageFile));
  }
}
