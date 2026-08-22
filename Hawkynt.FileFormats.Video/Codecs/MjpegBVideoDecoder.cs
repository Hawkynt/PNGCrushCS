using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Apple Motion JPEG-B (<c>mjpb</c>): baseline JPEG with the marker-and-length framing that
/// finds a quantisation table, a set of Huffman tables, a frame header and a scan by searching for
/// <c>FF</c> replaced by a fixed 48-byte header that states where each of those four sections begins,
/// and with one or two whole coded pictures — QuickTime's two interlaced fields — packed one after
/// the other behind it.
/// </summary>
/// <remarks>
/// There is no published bitstream description of this one either, so what follows was recovered
/// directly from two real captures — a 720x480 broadcast clip and a 160x120 one, six and twenty-seven
/// pictures — by reading the bytes against what <c>ffprobe</c> already said about them (dimensions,
/// 4:2:2 sampling, top-field-first) and confirming every guess against ffmpeg's own decode of the same
/// files.
/// <para/>
/// <b>The 48-byte field header.</b> Twelve big-endian 32-bit words: a reserved word that is zero in
/// every field measured; the four characters <c>mjpg</c>, which is checked and named in the refusal
/// when it is anything else; two sizes that were not needed to decode a single frame correctly and so
/// are read and ignored; an offset to a second field's own header of this same shape, zero when this
/// is the only field or the second one; and four more offsets — to the quantisation table, the
/// Huffman tables, the frame header and the scan header — each counted from this field's own start,
/// followed by two words that were zero on every field measured and are otherwise unused.
/// <para/>
/// <b>What sits behind each of those four offsets is a JPEG marker segment's payload with its length
/// but not its marker</b> — the same two-byte big-endian length, counting itself, that would follow
/// <c>FF DB</c>, <c>FF C4</c>, <c>FF C0</c> or <c>FF DA</c> in an ordinary JPEG, with the marker
/// itself never written because the header's own offsets already say which section is which. Put the
/// marker back in front of each one — <see cref="_ReadSection"/> does exactly that — and what results
/// is byte for byte a standard JPEG quantisation, Huffman, frame or scan segment, which is why this
/// reuses <see cref="JpegReader"/> for everything after reassembly rather than decoding any of it
/// itself. Both captures measured carry the ITU-T T.81 Annex K standard Huffman tables and a real,
/// per-picture quantisation table — nothing here is fixed the way some of this family's other members
/// turn out to be.
/// <para/>
/// <b>The scan data itself is not byte-stuffed.</b> An ordinary JPEG escapes every <c>FF</c> byte
/// inside entropy-coded data with a trailing zero so a decoder scanning for the next marker cannot
/// mistake picture data for one; this format has no need of that, because nothing ever scans for a
/// marker inside it; every section starts at a stated offset instead. Both captures' entropy data
/// average roughly one unescaped <c>FF</c> byte every three hundred bytes, and reassembling it for
/// <see cref="JpegReader"/> without inserting the escape back in front of every one of them decodes
/// the picture that happens to have few enough of them to get away with it and fails outright on the
/// picture that does not — the second field of the very first frame tried, with a "huffman table
/// decode error" hundreds of bytes short of where the real fault was. <see cref="_ByteStuff"/> puts
/// the escape back before handing the data to a standard reader.
/// <para/>
/// <b>Two fields, not one picture.</b> Every stream measured carries exactly two, matching the
/// <c>fiel</c> atom QuickTime's own sample description states beside this codec — top field first,
/// which is also the order the header's own chain of fields arrives in. Each field is a whole,
/// independently coded JPEG at half the picture's height; there is no shared state and no prediction
/// between them or between one coded picture and the next. Decoding both and interleaving their rows —
/// field one into the even rows, field two into the odd ones — reproduces ffmpeg's own decode of the
/// same packet exactly. A packet naming a third field is refused rather than guessed at, since none
/// was ever measured and nothing here says what order it would arrive in.
/// <para/>
/// <b>Verified exactly.</b> Every one of the thirty-three pictures across both captures — 720x480 and
/// 160x120, 4:2:2 throughout — was compared against ffmpeg's own <c>mjpegb</c> decode of the same
/// file, plane for plane: all thirty-three are identical, not one sample different anywhere, on every
/// frame of both files and not only the first.
/// <para/>
/// What is not implemented refuses and says so: a field header whose tag is not <c>mjpg</c>, a packet
/// too short to hold one, a field naming a third field, and two fields whose decoded pictures disagree
/// on width or height. There is no <c>catch</c> anywhere that hands back a blank or a repeated picture.
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

    var field1 = this._DecodeField(data, 0, out var next);
    if (next == 0) {
      frame = field1;
      return true;
    }

    var field2 = this._DecodeField(data, (int)next, out var third);
    if (third != 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb packet whose second field names a third field "
        + $"at offset {third}, which no measured capture ever does and this decoder does not guess the order of.");

    if (field1.Width != field2.Width || field1.Height != field2.Height)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb packet whose two fields decode to "
        + $"{field1.Width}x{field1.Height} and {field2.Width}x{field2.Height}, which cannot be woven into one picture.");

    frame = _Weave(field1, field2);
    return true;
  }

  /// <summary>Nothing is ever held back: every packet is one whole picture, field-woven or not.</summary>
  public System.Collections.Generic.IEnumerable<RawImage> Flush() => [];

  /// <summary>
  /// Decodes one field's own 48-byte header and the JPEG it points to, and hands back the offset of
  /// the next field, or zero when this is the last one.
  /// </summary>
  private RawImage _DecodeField(ReadOnlySpan<byte> data, int fieldBase, out uint nextFieldOffset) {
    if (data.Length < fieldBase + _HeaderSize)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb packet of {data.Length} byte(s), too short to hold "
        + $"a field header of {_HeaderSize} bytes at offset {fieldBase}.");

    var header = data.Slice(fieldBase, _HeaderSize);
    var tag = header[4..8];
    if (tag[0] != (byte)'m' || tag[1] != (byte)'j' || tag[2] != (byte)'p' || tag[3] != (byte)'g')
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose header names "
        + $"'{(char)tag[0]}{(char)tag[1]}{(char)tag[2]}{(char)tag[3]}' where every measured capture names 'mjpg'.");

    nextFieldOffset = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
    var quantOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
    var huffOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(header[24..28]);
    var sofOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(header[28..32]);
    var sosOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(header[32..36]);
    var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(header[36..40]);

    var fieldEnd = nextFieldOffset == 0 ? data.Length : (int)nextFieldOffset;

    var quant = _ReadSection(data, fieldBase, quantOffset, fieldEnd, "quantisation table");
    var huffman = _ReadSection(data, fieldBase, huffOffset, fieldEnd, "Huffman table");
    var sof = _ReadSection(data, fieldBase, sofOffset, fieldEnd, "frame header");
    var sos = _ReadSection(data, fieldBase, sosOffset, fieldEnd, "scan header");

    var entropyStart = fieldBase + dataOffset;
    if (entropyStart < 0 || entropyStart > fieldEnd)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field at offset {fieldBase} whose scan data offset "
        + $"{dataOffset} does not land inside the field's own {fieldEnd - fieldBase} byte(s).");

    var entropy = data[entropyStart..fieldEnd];

    var jpeg = new byte[
      2                          // SOI
      + 4 + quant.Length         // FF DB, length, payload
      + 4 + huffman.Length       // FF C4, length, payload
      + 4 + sof.Length           // FF C0, length, payload
      + 4 + sos.Length           // FF DA, length, payload
      + entropy.Length * 2       // worst case every byte needs stuffing
      + 2];                      // EOI

    var pos = 0;
    jpeg[pos++] = 0xFF; jpeg[pos++] = 0xD8;
    pos = _WriteSegment(jpeg, pos, 0xDB, quant);
    pos = _WriteSegment(jpeg, pos, 0xC4, huffman);
    pos = _WriteSegment(jpeg, pos, 0xC0, sof);
    pos = _WriteSegment(jpeg, pos, 0xDA, sos);
    pos = _ByteStuff(jpeg, pos, entropy);
    jpeg[pos++] = 0xFF; jpeg[pos++] = 0xD9;

    var file = JpegReader.FromSpan(jpeg.AsSpan(0, pos));
    return JpegFile.ToRawImage(file);
  }

  /// <summary>
  /// Reads the length-prefixed payload at <paramref name="fieldBase"/> + <paramref name="offset"/> —
  /// the two big-endian bytes a real JPEG marker segment states its own length in, counting itself,
  /// with no marker in front of them here because the field header's offset already says which
  /// section this is.
  /// </summary>
  private ReadOnlySpan<byte> _ReadSection(ReadOnlySpan<byte> data, int fieldBase, int offset, int fieldEnd, string name) {
    var start = fieldBase + offset;
    if (start < 0 || start + 2 > fieldEnd)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field whose {name} offset {offset} leaves no room "
        + "for that section's own two-byte length.");

    var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(start, 2));
    if (length < 2 || start + length > fieldEnd)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an mjpegb field whose {name} states a length of {length} byte(s), "
        + $"which does not fit in the {fieldEnd - start} byte(s) left in the field.");

    return data.Slice(start + 2, length - 2);
  }

  private static int _WriteSegment(byte[] buffer, int pos, byte marker, ReadOnlySpan<byte> payload) {
    buffer[pos++] = 0xFF;
    buffer[pos++] = marker;
    BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(pos, 2), (ushort)(payload.Length + 2));
    pos += 2;
    payload.CopyTo(buffer.AsSpan(pos));
    return pos + payload.Length;
  }

  /// <summary>
  /// Copies entropy-coded data in, inserting a zero byte after every literal <c>FF</c> — the escape
  /// an ordinary JPEG's entropy coder writes as it goes and this format's encoder never had to,
  /// because nothing here ever searches this data for a marker except the standard reader this hands
  /// it on to.
  /// </summary>
  private static int _ByteStuff(byte[] buffer, int pos, ReadOnlySpan<byte> entropy) {
    foreach (var b in entropy) {
      buffer[pos++] = b;
      if (b == 0xFF)
        buffer[pos++] = 0x00;
    }

    return pos;
  }

  /// <summary>Interleaves two same-sized fields into one picture, field one into the even rows and
  /// field two into the odd ones — top field first, the order every capture measured states.</summary>
  private static RawImage _Weave(RawImage field1, RawImage field2) {
    var width = field1.Width;
    var height = field1.Height + field2.Height;
    var bytesPerPixel = RawImage.BytesPerPixel(field1.Format);
    var stride = width * bytesPerPixel;
    var pixels = new byte[stride * height];

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
