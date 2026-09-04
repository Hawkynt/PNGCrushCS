using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Ogg.Tests;

/// <summary>
/// What survives a demux and a mux back into Ogg — the film, and what the file says about itself.
/// </summary>
/// <remarks>
/// The half of the pair the reader's own fixture cannot see. A demuxer that reads a comment header
/// and a muxer that writes none both pass their own tests, and the title that crossed the first and
/// not the second is invisible in either: Ogg was the last container here whose reader filled in a
/// title, an artist, an album and a run of annotations and whose writer put none of them in the file.
/// <para/>
/// The layouts asserted here were checked against ffmpeg's own muxer. Given
/// <c>-metadata title=… -metadata artist=…</c> it writes those tags into the comment header of every
/// logical bitstream in the file, keeps the vendor string for the library that wrote it, and ends a
/// Vorbis comment header — and only a Vorbis one — with the framing bit Vorbis I section 5.2 asks
/// for; Theora's and Opus's end at the last tag. <c>ffprobe -show_streams</c> reports the tags of
/// each bitstream against the stream it belongs to, and reports them for files written here.
/// </remarks>
[TestFixture]
public sealed class OggWriterTests {

  private static VideoMetadata _Rich() => new() {
    Title = "A film with a name",
    Artist = "Somebody",
    Album = "A series",
    EncodedBy = "the writer under test",
    CreationTime = new DateTimeOffset(2019, 4, 2, 11, 30, 0, TimeSpan.Zero),
    TextEntries = [new("DESCRIPTION", "a note"), new("COPYRIGHT", "nobody")],
  };

  /// <summary>A Theora file taken apart, which is what a remux begins with.</summary>
  private static (IReadOnlyList<MediaStreamInfo> Streams, List<CodedPacket> Packets) _Source(byte[]? file = null) {
    var container = OggReader.FromBytes(file ?? OggTestContainer.Theora());

    return (OggContainer.Streams(container), OggContainer.ReadPackets(container).ToList());
  }

  // ------------------------------------------------------------------------------------------
  // The round trip
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void ARemuxKeepsWhatTheReaderFound() {
    var (streams, packets) = _Source();

    var read = OggContainer.Metadata(OggReader.FromBytes(VideoIO.Mux<OggWriter>(streams, packets, _Rich())));

    Assert.Multiple(() => {
      Assert.That(read.Title, Is.EqualTo("A film with a name"));
      Assert.That(read.Artist, Is.EqualTo("Somebody"));
      Assert.That(read.Album, Is.EqualTo("A series"));
      Assert.That(read.EncodedBy, Is.EqualTo("the writer under test"));
      Assert.That(read.CreationTime, Is.EqualTo(new DateTimeOffset(2019, 4, 2, 11, 30, 0, TimeSpan.Zero)));
      Assert.That(read.TextEntries.Select(e => (e.Keyword, e.Text)),
        Is.SupersetOf(new[] { ("DESCRIPTION", "a note"), ("COPYRIGHT", "nobody") }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASecondRemuxSaysTheSameThingAsTheFirst() {
    // The reader files every tag it reads under its own name as well as under the field it fills, so
    // a second pass is handed a TITLE annotation and a title. Writing both would double the tag.
    var (streams, packets) = _Source();
    var once = VideoIO.Mux<OggWriter>(streams, packets, _Rich());

    var read = OggReader.FromBytes(once);
    var twice = VideoIO.Mux<OggWriter>(OggContainer.Streams(read), OggContainer.ReadPackets(read).ToList(), OggContainer.Metadata(read));

    Assert.That(_Tags(twice, 0x81, "theora"u8), Is.EqualTo(_Tags(once, 0x81, "theora"u8)).AsCollection);
  }

  [Test]
  [Category("Unit")]
  public void TheFilmItselfIsUnchangedByTheComment() {
    var (streams, packets) = _Source();
    var announced = VideoIO.Mux<OggWriter>(streams, packets, _Rich());
    var silent = VideoIO.Mux<OggWriter>(streams, packets, VideoMetadata.Empty);

    var fromAnnounced = OggContainer.ReadPackets(OggReader.FromBytes(announced)).ToList();
    var fromSilent = OggContainer.ReadPackets(OggReader.FromBytes(silent)).ToList();

    Assert.That(fromAnnounced, Has.Count.EqualTo(fromSilent.Count));
    for (var i = 0; i < fromAnnounced.Count; ++i)
      Assert.Multiple(() => {
        Assert.That(fromAnnounced[i].Data.ToArray(), Is.EqualTo(fromSilent[i].Data.ToArray()).AsCollection, $"packet {i}");
        Assert.That(fromAnnounced[i].PresentationTimestamp, Is.EqualTo(fromSilent[i].PresentationTimestamp), $"packet {i}");
      });
  }

  [Test]
  [Category("Unit")]
  public void MetadataWithNothingToSayLeavesTheSourceCommentHeaderAlone() {
    // A file whose source said nothing should come out looking like the file that went in, vendor
    // string included, rather than one this package signed on the way through.
    var (streams, packets) = _Source();
    var metadata = new VideoMetadata { Streams = [new(0, MediaStreamKind.Video, CodecTag.None)] };

    var written = VideoIO.Mux<OggWriter>(streams, packets, metadata);

    Assert.Multiple(() => {
      Assert.That(_Tags(written, 0x81, "theora"u8), Is.Empty);
      Assert.That(_Vendor(written, 0x81, "theora"u8), Is.EqualTo("test"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnAnnotationUnderAFieldsOwnNameIsNotWrittenTwice() {
    var (streams, packets) = _Source();
    var metadata = new VideoMetadata { Title = "The one title", TextEntries = [new("title", "another title")] };

    var written = VideoIO.Mux<OggWriter>(streams, packets, metadata);

    Assert.That(_Tags(written, 0x81, "theora"u8), Is.EqualTo(new[] { "TITLE=The one title" }).AsCollection);
  }

  [Test]
  [Category("Unit")]
  public void AnAnnotationWhoseKeywordIsNoFieldNameIsDroppedRatherThanWrittenAsOne() {
    // A field name is printable ASCII without an equals sign — Vorbis comment specification section
    // 5.4.2.1. A keyword carrying one would split into a different tag when read back.
    var (streams, packets) = _Source();
    var metadata = new VideoMetadata { Title = "A film", TextEntries = [new("BAD=NAME", "x"), new("GOOD", "y")] };

    var written = VideoIO.Mux<OggWriter>(streams, packets, metadata);

    Assert.That(_Tags(written, 0x81, "theora"u8), Is.EqualTo(new[] { "TITLE=A film", "GOOD=y" }).AsCollection);
  }

  [Test]
  [Category("Unit")]
  public void TextThatIsNotAsciiCrossesAsItself() {
    // Every string in a comment header is UTF-8, so a title is whatever the source called the film
    // rather than whatever survived a code page.
    var (streams, packets) = _Source();
    var metadata = new VideoMetadata { Title = "Fahrstuhl nach oben — 上へ", Artist = "Ünnamed" };

    var read = OggContainer.Metadata(OggReader.FromBytes(VideoIO.Mux<OggWriter>(streams, packets, metadata)));

    Assert.Multiple(() => {
      Assert.That(read.Title, Is.EqualTo("Fahrstuhl nach oben — 上へ"));
      Assert.That(read.Artist, Is.EqualTo("Ünnamed"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ACommentTooBigForOnePageIsLacedAcrossSeveral() {
    // A page holds 255 segments of 255 bytes and no more, so an annotation of any size at all makes
    // the comment header a packet that has to be continued — which is the one thing about writing a
    // long header that can go wrong.
    var (streams, packets) = _Source();
    var long_ = new string('x', 200_000);
    var metadata = new VideoMetadata { Title = "A film", TextEntries = [new("DESCRIPTION", long_)] };

    var written = VideoIO.Mux<OggWriter>(streams, packets, metadata);
    var read = OggContainer.Metadata(OggReader.FromBytes(written));

    Assert.Multiple(() => {
      Assert.That(read.Title, Is.EqualTo("A film"));
      Assert.That(read.TextEntries.Select(e => (e.Keyword, e.Text)), Does.Contain(("DESCRIPTION", long_)));
      Assert.That(OggContainer.ReadPackets(OggReader.FromBytes(written)).ToList(), Has.Count.EqualTo(packets.Count));
    });
  }

  // ------------------------------------------------------------------------------------------
  // The shape of the header itself
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void EveryBitstreamOfTheFileCarriesTheComment() {
    // What the file says about itself is the file's, and Ogg has nowhere to put it but the bitstreams
    // it multiplexes. ffmpeg writes it into every one of them, and so does this.
    var (streams, packets) = _Source(_TheoraWithVorbis());

    var written = VideoIO.Mux<OggWriter>(streams, packets, _Rich());

    Assert.Multiple(() => {
      Assert.That(_Tags(written, 0x81, "theora"u8), Does.Contain("TITLE=A film with a name"));
      Assert.That(_Tags(written, 0x03, "vorbis"u8), Does.Contain("TITLE=A film with a name"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AVorbisCommentHeaderEndsWithItsFramingBit() {
    // Vorbis I section 5.2 ends the comment header with a set framing bit and neither Theora's
    // section 6.2 nor RFC 7845 section 5.2 has one. libvorbis refuses a header without it.
    var (streams, packets) = _Source(_TheoraWithVorbis());

    var written = VideoIO.Mux<OggWriter>(streams, packets, _Rich());

    Assert.Multiple(() => {
      Assert.That(_AfterTags(written, 0x03, "vorbis"u8), Is.EqualTo((byte)0x01));
      Assert.That(_AfterTags(written, 0x81, "theora"u8), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void AnOpusBitstreamKeepsItsOwnMagic() {
    var (streams, packets) = _Source(_Opus());

    var written = VideoIO.Mux<OggWriter>(streams, packets, _Rich());

    Assert.Multiple(() => {
      Assert.That(_Tags(written, null, "OpusTags"u8), Does.Contain("ARTIST=Somebody"));
      Assert.That(_AfterTags(written, null, "OpusTags"u8), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void ABitstreamWithNoCommentHeaderIsWrittenAsItArrived() {
    // FLAC keeps its comment in a metadata block rather than in a packet of its own, and a mapping
    // nothing here recognises has no comment header at all. Neither is a place to write into, and
    // neither is touched.
    var (streams, packets) = _Source(_Flac());

    var written = VideoIO.Mux<OggWriter>(streams, packets, _Rich());

    Assert.That(OggContainer.Streams(OggReader.FromBytes(written))[0].CodecPrivateData.ToArray(),
      Is.EqualTo(streams[0].CodecPrivateData.ToArray()).AsCollection);
  }

  // ------------------------------------------------------------------------------------------
  // Files and readings
  // ------------------------------------------------------------------------------------------

  private static byte[] _TheoraWithVorbis() => OggTestContainer.Build(
    new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.TheoraIdentification()] },
    new() { Serial = 2, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.VorbisIdentification()] },
    new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.TheoraComment(), OggTestContainer.TheoraSetup()] },
    new() { Serial = 2, Sequence = 1, Granule = 0, Packets = [OggTestContainer.VorbisComment(), OggTestContainer.VorbisSetup()] },
    new() { Serial = 1, Sequence = 2, Granule = 64, EndOfStream = true, Packets = [OggTestContainer.TheoraFrame()] },
    new() { Serial = 2, Sequence = 2, Granule = 1024, EndOfStream = true, Packets = [OggTestContainer.VorbisSetup(20)] });

  private static byte[] _Opus() => OggTestContainer.Build(
    new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.OpusHead()] },
    new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.OpusTags()] },
    new() { Serial = 1, Sequence = 2, Granule = 1272, EndOfStream = true, Packets = [OggTestContainer.UnknownPacket()] });

  private static byte[] _Flac() => OggTestContainer.Build(
    new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [OggTestContainer.FlacMapping()] },
    new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [OggTestContainer.FlacMetadataBlock()] },
    new() { Serial = 1, Sequence = 2, Granule = 4096, EndOfStream = true, Packets = [OggTestContainer.FlacFrame()] });

  /// <summary>
  /// Reads a comment header out of a written file, the way something that is not this package would.
  /// </summary>
  /// <remarks>
  /// Straight off the bytes and past the pages rather than through <see cref="OggReader"/>. A test
  /// that read the header back with the reader that produced the bug would agree with it whatever
  /// either of them did, which is exactly how the bug lasted.
  /// </remarks>
  private static (string Vendor, List<string> Tags, byte? Trailing) _Comment(byte[] file, byte? marker, ReadOnlySpan<byte> magic) {
    var head = new byte[magic.Length + (marker == null ? 0 : 1)];
    if (marker != null)
      head[0] = marker.Value;
    magic.CopyTo(head.AsSpan(marker == null ? 0 : 1));

    var at = _IndexOf(file, head);
    Assert.That(at, Is.GreaterThanOrEqualTo(0), $"The file holds no comment header for {Encoding.ASCII.GetString(magic)}.");
    at += head.Length;

    var vendor = _String(file, ref at);
    var count = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
    at += 4;

    var tags = new List<string>();
    for (var i = 0U; i < count; ++i)
      tags.Add(_String(file, ref at));

    // A page's own header begins with the capture pattern, so a byte belonging to the comment packet
    // is one that is not the start of the next page.
    var trailing = at + 4 <= file.Length && !file.AsSpan(at, 4).SequenceEqual("OggS"u8) ? file[at] : (byte?)null;
    return (vendor, tags, trailing);
  }

  private static List<string> _Tags(byte[] file, byte? marker, ReadOnlySpan<byte> magic) => _Comment(file, marker, magic).Tags;

  private static string _Vendor(byte[] file, byte? marker, ReadOnlySpan<byte> magic) => _Comment(file, marker, magic).Vendor;

  private static byte? _AfterTags(byte[] file, byte? marker, ReadOnlySpan<byte> magic) => _Comment(file, marker, magic).Trailing;

  private static string _String(byte[] file, ref int at) {
    var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
    at += 4;
    var value = Encoding.UTF8.GetString(file, at, length);
    at += length;
    return value;
  }

  private static int _IndexOf(byte[] file, ReadOnlySpan<byte> needle) {
    for (var i = 0; i + needle.Length <= file.Length; ++i)
      if (file.AsSpan(i, needle.Length).SequenceEqual(needle))
        return i;

    return -1;
  }
}
