using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Encodes Apple Motion JPEG-B (<c>mjpb</c>) samples.</summary>
/// <remarks>
/// Apple Motion JPEG-B carries the contents of JPEG DQT, DHT, SOF and SOS segments without their
/// marker bytes, locates those sections with a 48-byte big-endian header, and stores entropy data
/// without JPEG byte stuffing. This encoder deliberately emits the progressive/single-field form:
/// every input picture is one independently coded field, with its tables inline and all offsets that
/// the QuickTime specification requires aligned to a 16-byte boundary. The actual JPEG coding is
/// delegated to the image package's managed baseline writer; this class only converts the resulting
/// JPEG framing into Motion JPEG-B framing.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class MjpegBVideoEncoder : IVideoCodecEncoder<MjpegBVideoEncoder> {

  private static readonly CodecTag _codec = CodecTag.FromCharacters("mjpb");
  private const int _HEADER_SIZE = 48;
  private const int _ALIGNMENT = 16;

  private readonly MediaStreamInfo _stream;

  private MjpegBVideoEncoder(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Apple Motion JPEG-B can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Apple Motion JPEG-B encoder needs the output dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _codec,
      Handler = _codec,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Apple Motion JPEG-B";

  public static CodecTag Codec => _codec;

  public static MjpegBVideoEncoder Create(MediaStreamInfo stream) => new(stream);

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder was created for {this._stream.Width}x{this._stream.Height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var jpeg = FormatIO.Encode<JpegFile>(frame);
    var parts = _SplitBaselineJpeg(jpeg);
    var data = _BuildField(parts);

    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private static byte[] _BuildField(JpegParts parts) {
    var quantOffset = _HEADER_SIZE;
    var huffmanOffset = _Align(checked(quantOffset + parts.Quantization.Length));
    var frameOffset = checked(huffmanOffset + parts.Huffman.Length);
    var scanOffset = checked(frameOffset + parts.Frame.Length);
    var dataOffset = _Align(checked(scanOffset + parts.Scan.Length));
    var fieldSize = checked(dataOffset + parts.Entropy.Length);
    var paddedFieldSize = _Align(fieldSize);

    var result = new byte[paddedFieldSize];
    var span = result.AsSpan();

    "mjpg"u8.CopyTo(span[4..8]);
    BinaryPrimitives.WriteUInt32BigEndian(span[8..12], checked((uint)fieldSize));
    BinaryPrimitives.WriteUInt32BigEndian(span[12..16], checked((uint)paddedFieldSize));
    // Offset to a second field stays zero: this writer emits one progressive field per sample.
    BinaryPrimitives.WriteUInt32BigEndian(span[20..24], checked((uint)quantOffset));
    BinaryPrimitives.WriteUInt32BigEndian(span[24..28], checked((uint)huffmanOffset));
    BinaryPrimitives.WriteUInt32BigEndian(span[28..32], checked((uint)frameOffset));
    BinaryPrimitives.WriteUInt32BigEndian(span[32..36], checked((uint)scanOffset));
    BinaryPrimitives.WriteUInt32BigEndian(span[36..40], checked((uint)dataOffset));

    parts.Quantization.CopyTo(span[quantOffset..]);
    parts.Huffman.CopyTo(span[huffmanOffset..]);
    parts.Frame.CopyTo(span[frameOffset..]);
    parts.Scan.CopyTo(span[scanOffset..]);
    parts.Entropy.CopyTo(span[dataOffset..]);

    return result;
  }

  /// <summary>
  /// Extracts the marker payloads a Format-B field carries. DQT and DHT may each occur more than once
  /// in an ordinary JPEG, so their table payloads are coalesced behind one Format-B length word.
  /// </summary>
  private static JpegParts _SplitBaselineJpeg(ReadOnlySpan<byte> jpeg) {
    if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
      throw new InvalidDataException("The managed JPEG encoder did not produce a JPEG SOI marker.");

    var quantization = new List<byte[]>();
    var huffman = new List<byte[]>();
    byte[]? frame = null;
    byte[]? scan = null;
    byte[]? entropy = null;

    var position = 2;
    while (position < jpeg.Length) {
      if (jpeg[position++] != 0xFF)
        throw new InvalidDataException($"The managed JPEG encoder produced data outside a marker at offset {position - 1}.");

      while (position < jpeg.Length && jpeg[position] == 0xFF)
        ++position;
      if (position >= jpeg.Length)
        break;

      var marker = jpeg[position++];
      if (marker == 0xD9)
        break;
      if (marker is 0xD8 or >= 0xD0 and <= 0xD7)
        throw new InvalidDataException($"The managed JPEG encoder produced unexpected standalone marker FF {marker:X2} before its scan.");
      if (position + 2 > jpeg.Length)
        throw new InvalidDataException($"JPEG marker FF {marker:X2} has no length word.");

      var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[position..]);
      if (length < 2 || position + length > jpeg.Length)
        throw new InvalidDataException($"JPEG marker FF {marker:X2} states an invalid length of {length} bytes.");

      var section = jpeg.Slice(position, length);
      var payload = section[2..];
      switch (marker) {
        case 0xDB:
          quantization.Add(payload.ToArray());
          break;
        case 0xC4:
          huffman.Add(payload.ToArray());
          break;
        case 0xC0:
          if (frame != null)
            throw new NotSupportedException("Apple Motion JPEG-B encoding supports one baseline JPEG frame header per field.");
          frame = section.ToArray();
          break;
        case 0xDA:
          if (scan != null)
            throw new NotSupportedException("Apple Motion JPEG-B encoding supports one JPEG scan per field.");
          scan = section.ToArray();
          entropy = _UnstuffEntropy(jpeg[(position + length)..]);
          position = jpeg.Length;
          continue;
        case 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF:
          throw new NotSupportedException($"Apple Motion JPEG-B encoding needs baseline JPEG, but the JPEG writer produced SOF marker FF {marker:X2}.");
        case 0xDD:
          throw new NotSupportedException("Apple Motion JPEG-B encoding does not emit JPEG restart markers.");
        default:
          // APPn, COM and other metadata have no marker representation in Format B and are omitted.
          break;
      }

      position += length;
    }

    if (quantization.Count == 0 || huffman.Count == 0 || frame == null || scan == null || entropy == null)
      throw new InvalidDataException("The managed JPEG encoder did not produce the DQT, DHT, SOF0, SOS and entropy sections Motion JPEG-B requires.");

    return new(
      _LengthPrefixed(quantization, "quantization"),
      _LengthPrefixed(huffman, "Huffman"),
      frame,
      scan,
      entropy);
  }

  private static byte[] _UnstuffEntropy(ReadOnlySpan<byte> data) {
    var result = new byte[data.Length];
    var written = 0;

    for (var i = 0; i < data.Length; ++i) {
      var value = data[i];
      if (value != 0xFF) {
        result[written++] = value;
        continue;
      }

      if (++i >= data.Length)
        throw new InvalidDataException("JPEG entropy data ends with an incomplete FF escape.");

      var escaped = data[i];
      if (escaped == 0x00) {
        result[written++] = 0xFF;
        continue;
      }
      if (escaped == 0xD9)
        return result.AsSpan(0, written).ToArray();
      if (escaped is >= 0xD0 and <= 0xD7)
        throw new NotSupportedException("Apple Motion JPEG-B encoding does not emit JPEG restart markers.");

      throw new NotSupportedException($"Apple Motion JPEG-B encoding supports one JPEG scan; entropy was followed by marker FF {escaped:X2}.");
    }

    throw new InvalidDataException("JPEG entropy data has no EOI marker.");
  }

  private static byte[] _LengthPrefixed(List<byte[]> payloads, string name) {
    var payloadLength = 0;
    foreach (var payload in payloads)
      payloadLength = checked(payloadLength + payload.Length);

    var length = checked(payloadLength + 2);
    if (length > ushort.MaxValue)
      throw new NotSupportedException($"The JPEG {name} tables need {length} bytes, too large for one Motion JPEG-B section.");

    var result = new byte[length];
    BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)length);
    var position = 2;
    foreach (var payload in payloads) {
      payload.CopyTo(result, position);
      position += payload.Length;
    }

    return result;
  }

  private static int _Align(int value) => checked((value + (_ALIGNMENT - 1)) & ~(_ALIGNMENT - 1));

  private sealed record JpegParts(byte[] Quantization, byte[] Huffman, byte[] Frame, byte[] Scan, byte[] Entropy);
}
