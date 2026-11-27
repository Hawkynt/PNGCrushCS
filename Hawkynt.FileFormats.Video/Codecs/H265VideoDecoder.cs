using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes H.265 / HEVC video, ITU-T H.265 | ISO/IEC 23008-2.
/// </summary>
/// <remarks>
/// The decoder reconstructs native pictures at eight, ten and twelve bits in every chroma format the
/// standard defines — monochrome, 4:2:0, 4:2:2 and 4:4:4 — including intra and inter slices,
/// reference-picture management, weighted prediction, CABAC, scaling lists, deblocking and
/// sample-adaptive offset. Tile and dependent-slice transport structure is handled in the same
/// picture decoder rather than flattened or silently ignored.
/// <para/>
/// Completed pictures are returned as native planar samples after both in-loop filters, in the
/// layout the sequence's own chroma format and depth name. RGB conversion remains a consumer-side
/// operation through <see cref="RawImageConverter"/>. Unsupported profile extensions still fail
/// explicitly rather than returning plausible partial pictures.
/// <para/>
/// The one deliberately narrow exception is the uniform 8-bit 4:2:0 PCM shape emitted by the managed
/// HEVC writer. That syntax leaves CABAC for each coding unit, carries raw samples and starts the
/// arithmetic registers again without resetting the probability contexts. The still-image HEVC core
/// already implements that standards-defined handoff and is reused here until the streaming frame
/// decoder exposes the same raw-sample handoff generically.
/// </remarks>
public sealed class H265VideoDecoder : IVideoCodecDecoder<H265VideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("hvc1"),
    CodecTag.FromCharacters("hev1"),
    CodecTag.FromCharacters("hvc2"),
    CodecTag.FromCharacters("hev2"),
    CodecTag.FromCharacters("HEVC"),
    CodecTag.FromCharacters("H265"),
    CodecTag.FromCharacters("h265"),
  ];

  private static readonly string[] _CodecIds = [
    "V_MPEGH/ISO/HEVC",
  ];

  private readonly ReadOnlyMemory<byte> _configurationData;
  private readonly H265DecoderConfiguration? _configuration;
  private readonly H265StreamDecoder _stream = new();
  private readonly Queue<RawImage> _ready = [];

  private H265VideoDecoder(ReadOnlyMemory<byte> configurationData) {
    this._configurationData = configurationData;
    this._configuration = H265DecoderConfiguration.TryParse(configurationData);

    var sets = new List<H265NalUnit>();
    foreach (var set in this._configuration?.ParameterSets ?? [])
      sets.Add(H265NalReader.Parse(set));
    this._stream.Decode(sets);
  }

  public static string CodecName
    => "H.265/HEVC (ITU-T H.265 | ISO/IEC 23008-2)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    foreach (var id in _CodecIds)
      if (string.Equals(stream.CodecId, id, StringComparison.OrdinalIgnoreCase))
        return true;

    return false;
  }

  public static H265VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(stream.CodecPrivateData);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    // The managed writer uses the exact uniform PCM subset the HEIF path already decodes. Keep this
    // before the general frame decoder: once that decoder has consumed pcm_flag its arithmetic state
    // has deliberately ended, so discovering afterwards that the raw handoff is unsupported is too
    // late to retry the access unit from a clean state.
    if (!this._configurationData.IsEmpty
        && H265PcmStillCodec.TryDecode(packet.Data, this._configurationData, out var pcm)) {
      this._stream.Flush();
      this._Collect();
      this._ready.Enqueue(pcm);

      frame = this._ready.Dequeue();
      return true;
    }

    this._stream.Decode(this._Split(packet.Data));
    this._stream.EndOfAccessUnit();
    this._Collect();

    if (this._ready.Count == 0) {
      frame = null!;
      return false;
    }

    frame = this._ready.Dequeue();
    return true;
  }

  public IEnumerable<RawImage> Flush() {
    this._stream.Flush();
    this._Collect();

    while (this._ready.Count > 0)
      yield return this._ready.Dequeue();
  }

  private void _Collect() {
    while (this._stream.TryTakePicture(out var picture, out var sps))
      this._ready.Enqueue(H265StreamDecoder.ToPlanarImage(picture, sps));
  }

  private IReadOnlyList<H265NalUnit> _Split(ReadOnlyMemory<byte> data) {
    if (this._configuration != null)
      return H265NalReader.SplitLengthPrefixed(data, this._configuration.LengthSize);

    if (data.Length > 0 && !H265NalReader.LooksLikeAnnexB(data.Span))
      throw new InvalidDataException(
        "This H.265 packet begins with neither a start code nor a length prefix this decoder could learn the size "
        + "of: the container stated no HEVCDecoderConfigurationRecord, so the packets were expected to be in the "
        + "Annex B byte stream format, and this one is not. The stream is length-prefixed and its configuration "
        + "record is missing.");

    return H265NalReader.SplitAnnexB(data);
  }
}
