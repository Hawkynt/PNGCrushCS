using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.Ioca;

/// <summary>Reads IBM IOCA images, whether wrapped in MO:DCA structured fields or standing alone.</summary>
/// <remarks>
/// There are two nestings here and the reader that stood in this place read neither. The outer one
/// is MO:DCA: a chain of structured fields, each two bytes of length that count themselves, the
/// introducer <c>0xD3</c>, a three-byte type, a flag byte and two reserved bytes. The picture's size
/// arrives in an Image Data Descriptor and its coding in one or more Image Picture Data fields.
/// <para/>
/// The inner one is IOCA's own: the Image Picture Data, concatenated, is a second chain of
/// self-defining fields — a one-byte code and a one-byte length, or <c>0xFE</c> and a second code
/// byte followed by two bytes of length. The coded raster is in the long-form Image Data fields of
/// that chain, and a decoder that starts at the beginning of the Image Picture Data is a whole
/// header early.
/// <para/>
/// Both chains are required to land exactly on their end, which is what lets this refuse a file of
/// some other format: nothing but a real one walks to the byte.
/// </remarks>
public static class IocaReader {

  /// <summary>Every MO:DCA structured field carries this after its length.</summary>
  private const byte _StructuredFieldIntroducer = 0xD3;

  /// <summary>Length, introducer, three-byte type, flags and two reserved bytes.</summary>
  private const int _StructuredFieldHeaderSize = 8;

  /// <summary>Image Data Descriptor — states the unit base, the resolutions and the size.</summary>
  private const int _TypeImageDataDescriptor = 0xA6FB;

  /// <summary>Image Picture Data — carries the IOCA self-defining fields.</summary>
  private const int _TypeImagePictureData = 0xEEFB;

  /// <summary>Begin Segment, the field a bare IOCA stream opens with.</summary>
  internal const byte SegmentBegin = 0x70;

  /// <summary>Image Size: unit base, X and Y resolution, X and Y size.</summary>
  private const byte _FieldImageSize = 0x94;

  /// <summary>Image Encoding: the compression identifier and the recording order.</summary>
  private const byte _FieldImageEncoding = 0x95;

  /// <summary>Image IDE Size: how many bits an image data element takes.</summary>
  private const byte _FieldIdeSize = 0x96;

  /// <summary>Introduces a two-byte code with a two-byte length behind it.</summary>
  private const byte _FieldLongForm = 0xFE;

  /// <summary>Image Data, long form.</summary>
  private const int _FieldImageData = 0xFE92;

  /// <summary>Band Image Data, long form.</summary>
  private const int _FieldBandImageData = 0xFE9C;

  /// <summary>The only compression identifier this decodes: G4 two-dimensional, ITU-T T.6.</summary>
  /// <remarks>
  /// Taken from the one sample rather than from a table: its coded data, lifted out and handed to
  /// ImageMagick as a Group 4 TIFF strip of the stated size, comes back as a legible page. The other
  /// identifiers the architecture defines are refused by name rather than guessed at, because a
  /// wrong guess here draws a full page of noise at exactly the right size.
  /// </remarks>
  internal const byte CompressionG4 = 0x82;

  /// <summary>One bit an image data element, which is what a bilevel picture uses.</summary>
  internal const byte BilevelIdeSize = 1;

  public static IocaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IOCA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IocaFile FromStream(Stream stream) {
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

  public static IocaFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _StructuredFieldHeaderSize)
      throw new InvalidDataException(
        $"IOCA data too small: expected at least {_StructuredFieldHeaderSize} bytes, got {data.Length}.");

    var content = data[0] == SegmentBegin ? data.ToArray() : _CollectImagePictureData(data);

    return _ReadImageContent(content);
  }

  public static IocaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Walks the MO:DCA chain and joins what the Image Picture Data fields carry.</summary>
  private static byte[] _CollectImagePictureData(ReadOnlySpan<byte> data) {

    if (data[2] != _StructuredFieldIntroducer)
      throw new InvalidDataException(
        "Not an IOCA image: the file neither opens with a Begin Segment nor carries the MO:DCA "
        + $"structured field introducer 0x{_StructuredFieldIntroducer:X2} at offset 2 "
        + $"(found 0x{data[2]:X2}).");

    var joined = new List<byte>();
    var sawImagePictureData = false;
    var at = 0;

    while (at < data.Length) {
      if (at + _StructuredFieldHeaderSize > data.Length)
        throw new InvalidDataException(
          $"IOCA structured field chain runs off the end: {data.Length - at} bytes left at offset {at}, "
          + $"which is less than the {_StructuredFieldHeaderSize}-byte field header.");

      var length = (data[at] << 8) | data[at + 1];
      if (length < _StructuredFieldHeaderSize)
        throw new InvalidDataException(
          $"IOCA structured field at offset {at} states a length of {length}, "
          + $"which is shorter than its own {_StructuredFieldHeaderSize}-byte header.");

      if (at + length > data.Length)
        throw new InvalidDataException(
          $"IOCA structured field at offset {at} states a length of {length}, "
          + $"which reaches past the end of the {data.Length}-byte file.");

      if (data[at + 2] != _StructuredFieldIntroducer)
        throw new InvalidDataException(
          $"IOCA structured field at offset {at} is missing the introducer "
          + $"0x{_StructuredFieldIntroducer:X2} (found 0x{data[at + 2]:X2}).");

      var type = (data[at + 3] << 8) | data[at + 4];
      var payload = data.Slice(at + _StructuredFieldHeaderSize, length - _StructuredFieldHeaderSize);

      switch (type) {
        case _TypeImagePictureData:
          sawImagePictureData = true;
          for (var i = 0; i < payload.Length; ++i)
            joined.Add(payload[i]);
          break;
        case _TypeImageDataDescriptor:
          // The size is stated a second time inside the image content, and that is the one the
          // coding is in step with, so this one is read only as far as checking it is there.
          if (payload.Length < 9)
            throw new InvalidDataException(
              $"IOCA Image Data Descriptor at offset {at} is {payload.Length} bytes, "
              + "which is too short to state a unit base, two resolutions and two sizes.");
          break;
      }

      at += length;
    }

    if (!sawImagePictureData)
      throw new InvalidDataException(
        "Not an IOCA image: the structured field chain carries no Image Picture Data.");

    return joined.ToArray();
  }

  /// <summary>Walks IOCA's own self-defining fields and decodes what they describe.</summary>
  private static IocaFile _ReadImageContent(ReadOnlySpan<byte> content) {

    var width = 0;
    var height = 0;
    int? compression = null;
    int? ideSize = null;
    var coded = new List<byte>();
    var at = 0;

    while (at < content.Length) {
      int code;
      int length;
      int bodyAt;

      if (content[at] == _FieldLongForm) {
        if (at + 4 > content.Length)
          throw new InvalidDataException(
            $"IOCA long-form self-defining field at offset {at} has no room for its code and length.");

        code = (content[at] << 8) | content[at + 1];
        length = (content[at + 2] << 8) | content[at + 3];
        bodyAt = at + 4;
      } else {
        if (at + 2 > content.Length)
          throw new InvalidDataException(
            $"IOCA self-defining field at offset {at} has no room for its code and length.");

        code = content[at];
        length = content[at + 1];
        bodyAt = at + 2;
      }

      if (bodyAt + length > content.Length)
        throw new InvalidDataException(
          $"IOCA self-defining field 0x{code:X2} at offset {at} states {length} bytes, "
          + $"which reaches past the end of the {content.Length}-byte image content.");

      var body = content.Slice(bodyAt, length);

      switch (code) {
        case _FieldImageSize:
          if (length < 9)
            throw new InvalidDataException(
              $"IOCA Image Size field states {length} bytes where a unit base, two resolutions and "
              + "two sizes take nine.");

          width = (body[5] << 8) | body[6];
          height = (body[7] << 8) | body[8];
          break;

        case _FieldImageEncoding:
          if (length < 2)
            throw new InvalidDataException(
              $"IOCA Image Encoding field states {length} bytes where a compression identifier and a "
              + "recording identifier take two.");

          compression = body[0];
          break;

        case _FieldIdeSize:
          if (length < 1)
            throw new InvalidDataException("IOCA Image IDE Size field states no bytes.");

          ideSize = body[0];
          break;

        case _FieldImageData:
        case _FieldBandImageData:
          for (var i = 0; i < body.Length; ++i)
            coded.Add(body[i]);
          break;
      }

      at = bodyAt + length;
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException(
        "Not an IOCA image: no Image Size field in it states a picture size, and a size is not guessed.");

    if (compression is null)
      throw new InvalidDataException(
        "Not an IOCA image: no Image Encoding field states how the raster is coded.");

    if (compression != CompressionG4)
      throw new InvalidDataException(
        $"IOCA compression 0x{compression:X2} is not decoded here; only 0x{CompressionG4:X2}, "
        + "G4 two-dimensional coding, has been checked against a file.");

    if (ideSize is not null and not BilevelIdeSize)
      throw new InvalidDataException(
        $"IOCA image data element size {ideSize} is not decoded here; only bilevel data, one bit an "
        + "element, has been checked against a file.");

    if (coded.Count == 0)
      throw new InvalidDataException("Not an IOCA image: the image content carries no Image Data.");

    var pixelData = CcittG4Decoder.Decode(coded.ToArray(), width, height, out var rowsDecoded);
    if (rowsDecoded != height)
      throw new InvalidDataException(
        $"IOCA G4 coding runs out after {rowsDecoded} of the {height} rows the Image Size field states.");

    return new() { Width = width, Height = height, PixelData = pixelData };
  }
}
