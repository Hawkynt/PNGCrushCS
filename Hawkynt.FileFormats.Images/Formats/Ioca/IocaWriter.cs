using System;
using System.Collections.Generic;
using FileFormat.Ccitt;

namespace FileFormat.Ioca;

/// <summary>Assembles a MO:DCA document around an IOCA image object.</summary>
/// <remarks>
/// What stood here wrote four bytes of width and height and then the bitmap, which is not a
/// structure the architecture defines anywhere. The reader beside it read the same four bytes, so
/// the pair agreed with each other and with nothing that exists. This writes the chain the reader
/// now walks and the one sample carries: a document, a page and an image object, an Image Data
/// Descriptor stating the size, and the raster G4-coded inside IOCA's own self-defining fields.
/// </remarks>
public static class IocaWriter {

  private const byte _Introducer = 0xD3;
  private const int _FieldHeaderSize = 8;

  private static readonly byte[] _BeginDocument = [0xA8, 0xA8];
  private static readonly byte[] _EndDocument = [0xA9, 0xA8];
  private static readonly byte[] _BeginPage = [0xA8, 0xAF];
  private static readonly byte[] _EndPage = [0xA9, 0xAF];
  private static readonly byte[] _BeginImageObject = [0xA8, 0xFB];
  private static readonly byte[] _EndImageObject = [0xA9, 0xFB];
  private static readonly byte[] _ImageDataDescriptor = [0xA6, 0xFB];
  private static readonly byte[] _ImagePictureData = [0xEE, 0xFB];

  /// <summary>Begin Image Content, whose one parameter byte is always 0xFF.</summary>
  private const byte _FieldBeginImageContent = 0x91;

  /// <summary>End Image Content.</summary>
  private const byte _FieldEndImageContent = 0x93;

  /// <summary>End Segment.</summary>
  private const byte _FieldSegmentEnd = 0x71;

  private const byte _FieldImageSize = 0x94;
  private const byte _FieldImageEncoding = 0x95;
  private const byte _FieldIdeSize = 0x96;

  /// <summary>Records are read in the order they are written, which is recording identifier 1.</summary>
  private const byte _RecordingSequential = 0x01;

  /// <summary>Resolution unit base 0 means ten inches, so 2000 units is 200 to the inch.</summary>
  private const byte _UnitBaseTenInches = 0x00;

  /// <summary>Units across ten inches — 200 dots to the inch, which is what a fax page is.</summary>
  private const int _Resolution = 2000;

  /// <summary>A structured field's length is two bytes, and the architecture stops short of 32768.</summary>
  private const int _MaximumFieldLength = 32767;

  /// <summary>How much coded data one Image Data field carries, kept well inside a field's length.</summary>
  private const int _CodedChunkSize = 8192;

  public static byte[] ToBytes(IocaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException($"An IOCA image cannot be {file.Width} by {file.Height}.", nameof(file));

    if (file.Width > ushort.MaxValue || file.Height > ushort.MaxValue)
      throw new ArgumentException(
        $"IOCA states its size in two bytes, so {file.Width} by {file.Height} cannot be written.", nameof(file));

    var stride = (file.Width + 7) / 8;
    var pixels = new byte[stride * file.Height];
    (file.PixelData ?? []).AsSpan(0, Math.Min(file.PixelData?.Length ?? 0, pixels.Length)).CopyTo(pixels);

    var coded = CcittG4Encoder.Encode(pixels, file.Width, file.Height);
    var descriptor = _Descriptor(file.Width, file.Height);

    var output = new List<byte>();
    _Field(output, _BeginDocument, []);
    _Field(output, _BeginPage, []);
    _Field(output, _BeginImageObject, []);
    _Field(output, _ImageDataDescriptor, descriptor);

    var content = new List<byte>();
    _ShortField(content, IocaReader.SegmentBegin, []);
    _ShortField(content, _FieldBeginImageContent, [0xFF]);
    _ShortField(content, _FieldImageSize, descriptor);
    _ShortField(content, _FieldImageEncoding, [IocaReader.CompressionG4, _RecordingSequential]);
    _ShortField(content, _FieldIdeSize, [IocaReader.BilevelIdeSize]);
    _Field(output, _ImagePictureData, content.ToArray());

    // Each Image Data field goes into its own Image Picture Data field, so no long-form field ever
    // straddles a structured field boundary — which is the shape the sample has and the shape the
    // reader's two-level walk expects.
    for (var at = 0; at < coded.Length; at += _CodedChunkSize) {
      var take = Math.Min(_CodedChunkSize, coded.Length - at);
      var chunk = new List<byte> { 0xFE, 0x92, (byte)(take >> 8), (byte)take };
      for (var i = 0; i < take; ++i)
        chunk.Add(coded[at + i]);
      _Field(output, _ImagePictureData, chunk.ToArray());
    }

    var trailer = new List<byte>();
    _ShortField(trailer, _FieldEndImageContent, []);
    _ShortField(trailer, _FieldSegmentEnd, []);
    _Field(output, _ImagePictureData, trailer.ToArray());

    _Field(output, _EndImageObject, []);
    _Field(output, _EndPage, []);
    _Field(output, _EndDocument, []);

    return output.ToArray();
  }

  /// <summary>Unit base, both resolutions and both sizes — nine bytes, the same in both nestings.</summary>
  private static byte[] _Descriptor(int width, int height) => [
    _UnitBaseTenInches,
    (byte)(_Resolution >> 8), unchecked((byte)_Resolution),
    (byte)(_Resolution >> 8), unchecked((byte)_Resolution),
    (byte)(width >> 8), (byte)width,
    (byte)(height >> 8), (byte)height,
  ];

  private static void _Field(List<byte> output, byte[] type, byte[] payload) {
    var length = _FieldHeaderSize + payload.Length;
    if (length > _MaximumFieldLength)
      throw new ArgumentException($"A MO:DCA structured field cannot be {length} bytes.", nameof(payload));

    output.Add((byte)(length >> 8));
    output.Add((byte)length);
    output.Add(_Introducer);
    output.Add(type[0]);
    output.Add(type[1]);
    output.Add(0x00);
    output.Add(0x00);
    output.Add(0x00);
    output.AddRange(payload);
  }

  private static void _ShortField(List<byte> output, byte code, byte[] body) {
    output.Add(code);
    output.Add((byte)body.Length);
    output.AddRange(body);
  }
}
