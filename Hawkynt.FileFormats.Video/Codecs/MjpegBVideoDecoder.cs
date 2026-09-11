using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Decodes Apple Motion JPEG-B (<c>mjpb</c>) samples.</summary>
/// <remarks>
/// Motion JPEG-B stores JPEG DQT, DHT, SOF and SOS segment contents without their marker bytes and
/// locates them with a 48-byte big-endian field header. Entropy data is likewise stored without JPEG
/// byte stuffing. One sample may contain one field or two independently coded fields; two fields are
/// woven top-field-first into one output picture.
/// <para/>
/// The field and padded-field sizes matter. Hardware encoders may append zero padding before the next
/// field or the end of the sample, and those bytes are not entropy data. The decoder therefore bounds
/// each JPEG reconstruction by the declared field size and uses the padded size and next-field offset
/// only to validate and locate the following field. Older locally-built vectors whose two size words
/// are zero remain accepted by deriving the field end from the next-field offset or packet end.
/// <para/>
/// A zero quantization or Huffman offset means the table lives in the QuickTime image description as
/// an <c>mjqt</c> or <c>mjht</c> extension. The common stream contract currently carries the sample
/// description verbatim but does not expose those extension payloads independently, so that variant
/// is refused explicitly rather than being misread as a table at offset zero.
/// </remarks>
public sealed class MjpegBVideoDecoder : IVideoCodecDecoder<MjpegBVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("mjpb");
  private const int _HeaderSize = 48;

  private readonly int _streamIndex;

  private MjpegBVideoDecoder(int streamIndex) => this._streamIndex = streamIndex;

  public static string CodecName => "Apple Motion JPEG-B";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static MjpegBVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;

    var field1 = this._DecodeField(data, 0, out var nextFieldBase);
    if (nextFieldBase == 0) {
      frame = field1;
      return true;
    }

    // The third field is refused before the second one is decoded, not after. A packet that names a
    // third field usually names it past its own end as well, and decoding first would report the
    // overrun -- true, but a consequence of the real problem and not the thing worth telling anyone.
    var thirdFieldBase = _PeekNextFieldOffset(data, nextFieldBase);
    if (thirdFieldBase != 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb packet whose second field names a third field at offset {thirdFieldBase}; this decoder supports one or two fields per sample.");

    var field2 = this._DecodeField(data, nextFieldBase, out _);

    if (field1.Width != field2.Width || field1.Height != field2.Height || field1.Format != field2.Format)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb packet whose two fields decode to incompatible pictures: "
        + $"{field1.Width}x{field1.Height} {field1.Format} and {field2.Width}x{field2.Height} {field2.Format}.");

    frame = _Weave(field1, field2);
    return true;
  }

  /// <summary>Nothing is ever held back: every packet is one whole picture, field-woven or not.</summary>
  public System.Collections.Generic.IEnumerable<RawImage> Flush() => [];

  /// <summary>
  /// The next-field offset a field header states, or zero when there is no header there to read.
  /// </summary>
  /// <remarks>
  /// A packet too short to hold the header is left to <see cref="_DecodeField"/>, which says so in
  /// those terms; answering zero here only means "no third field was named", which is the question
  /// being asked.
  /// </remarks>
  private static int _PeekNextFieldOffset(ReadOnlySpan<byte> data, int fieldBase) {
    if ((uint)fieldBase > (uint)data.Length || data.Length - fieldBase < _HeaderSize)
      return 0;

    var offset = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(fieldBase + 16, 4));
    return offset > int.MaxValue ? int.MaxValue : (int)offset;
  }

  private RawImage _DecodeField(ReadOnlySpan<byte> data, int fieldBase, out int nextFieldBase) {
    if ((uint)fieldBase > (uint)data.Length || data.Length - fieldBase < _HeaderSize)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb packet of {data.Length} byte(s), too short to hold a {_HeaderSize}-byte field header at offset {fieldBase}.");

    var header = data.Slice(fieldBase, _HeaderSize);
    var tag = header[4..8];
    if (!tag.SequenceEqual("mjpg"u8))
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose header does not contain the required 'mjpg' tag.");

    var declaredFieldSize = BinaryPrimitives.ReadUInt32BigEndian(header[8..12]);
    var declaredPaddedSize = BinaryPrimitives.ReadUInt32BigEndian(header[12..16]);
    var nextFieldOffset = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
    var quantOffset = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
    var huffOffset = BinaryPrimitives.ReadUInt32BigEndian(header[24..28]);
    var sofOffset = BinaryPrimitives.ReadUInt32BigEndian(header[28..32]);
    var sosOffset = BinaryPrimitives.ReadUInt32BigEndian(header[32..36]);
    var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(header[36..40]);

    var available = data.Length - fieldBase;
    var fieldSize = declaredFieldSize == 0
      ? nextFieldOffset == 0 ? available : this._CheckedExtent(nextFieldOffset, available, "next-field offset", fieldBase)
      : this._CheckedExtent(declaredFieldSize, available, "field size", fieldBase);

    if (fieldSize < _HeaderSize)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose field size is {fieldSize}, smaller than its {_HeaderSize}-byte header.");

    if (declaredPaddedSize != 0) {
      var paddedSize = this._CheckedExtent(declaredPaddedSize, available, "padded field size", fieldBase);
      if (paddedSize < fieldSize)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose padded size {paddedSize} is smaller than its field size {fieldSize}.");
      if (nextFieldOffset != 0 && paddedSize > nextFieldOffset)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose padded size {paddedSize} overlaps the next field at relative offset {nextFieldOffset}.");
    }

    if (nextFieldOffset == 0) {
      nextFieldBase = 0;
    } else {
      var next = this._CheckedExtent(nextFieldOffset, available, "next-field offset", fieldBase);
      if (next < fieldSize)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose next field starts at relative offset {next}, inside its {fieldSize}-byte image data.");
      nextFieldBase = checked(fieldBase + next);
    }

    if (quantOffset == 0 || huffOffset == 0) {
      var missing = quantOffset == 0 && huffOffset == 0 ? "mjqt and mjht" : quantOffset == 0 ? "mjqt" : "mjht";
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries an mjpegb field using the image-description default {missing} table extension(s), which this decoder does not yet import from codec-private data.");
    }

    var quant = this._ReadSection(data, fieldBase, quantOffset, fieldSize, "quantization table");
    var huffman = this._ReadSection(data, fieldBase, huffOffset, fieldSize, "Huffman table");
    var sof = this._ReadSection(data, fieldBase, sofOffset, fieldSize, "frame header");
    var sos = this._ReadSection(data, fieldBase, sosOffset, fieldSize, "scan header");

    var entropyRelative = this._CheckedOffset(dataOffset, fieldSize, "scan data", fieldBase, allowEnd: true);
    var entropyStart = checked(fieldBase + entropyRelative);
    var fieldEnd = checked(fieldBase + fieldSize);
    var entropy = data[entropyStart..fieldEnd];

    var jpeg = new byte[
      2
      + 4 + quant.Length
      + 4 + huffman.Length
      + 4 + sof.Length
      + 4 + sos.Length
      + entropy.Length * 2
      + 2];

    var position = 0;
    jpeg[position++] = 0xFF;
    jpeg[position++] = 0xD8;
    position = _WriteSegment(jpeg, position, 0xDB, quant);
    position = _WriteSegment(jpeg, position, 0xC4, huffman);
    position = _WriteSegment(jpeg, position, 0xC0, sof);
    position = _WriteSegment(jpeg, position, 0xDA, sos);
    position = _ByteStuff(jpeg, position, entropy);
    jpeg[position++] = 0xFF;
    jpeg[position++] = 0xD9;

    var file = JpegReader.FromSpan(jpeg.AsSpan(0, position));
    return JpegFile.ToRawImage(file);
  }

  private int _CheckedExtent(uint value, int available, string name, int fieldBase) {
    if (value > (uint)available)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose {name} is {value}, beyond the {available} byte(s) available from that field.");
    return (int)value;
  }

  private int _CheckedOffset(uint value, int fieldSize, string name, int fieldBase, bool allowEnd = false) {
    var valid = allowEnd ? value <= (uint)fieldSize : value < (uint)fieldSize;
    if (!valid)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose {name} offset {value} does not land inside its {fieldSize}-byte image data.");
    return (int)value;
  }

  private ReadOnlySpan<byte> _ReadSection(ReadOnlySpan<byte> data, int fieldBase, uint offset, int fieldSize, string name) {
    var relative = this._CheckedOffset(offset, fieldSize, name, fieldBase);
    if (fieldSize - relative < 2)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field whose {name} offset {offset} leaves no room for that section's two-byte length.");

    var start = checked(fieldBase + relative);
    var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(start, 2));
    if (length < 2 || length > fieldSize - relative)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field whose {name} states a length of {length} byte(s), which does not fit in the field.");

    return data.Slice(start + 2, length - 2);
  }

  private static int _WriteSegment(byte[] buffer, int position, byte marker, ReadOnlySpan<byte> payload) {
    buffer[position++] = 0xFF;
    buffer[position++] = marker;
    BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(position, 2), checked((ushort)(payload.Length + 2)));
    position += 2;
    payload.CopyTo(buffer.AsSpan(position));
    return position + payload.Length;
  }

  private static int _ByteStuff(byte[] buffer, int position, ReadOnlySpan<byte> entropy) {
    foreach (var value in entropy) {
      buffer[position++] = value;
      if (value == 0xFF)
        buffer[position++] = 0x00;
    }

    return position;
  }

  private static RawImage _Weave(RawImage field1, RawImage field2) {
    var width = field1.Width;
    var height = field1.Height + field2.Height;
    var bytesPerPixel = RawImage.BytesPerPixel(field1.Format);
    var stride = width * bytesPerPixel;
    var pixels = new byte[checked(stride * height)];

    for (var y = 0; y < field1.Height; ++y)
      field1.PixelData.AsSpan(y * stride, stride).CopyTo(pixels.AsSpan((y * 2) * stride, stride));
    for (var y = 0; y < field2.Height; ++y)
      field2.PixelData.AsSpan(y * stride, stride).CopyTo(pixels.AsSpan((y * 2 + 1) * stride, stride));

    return new() {
      Width = width,
      Height = height,
      Format = field1.Format,
      PixelData = pixels,
    };
  }
}
