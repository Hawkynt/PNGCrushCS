using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Flv.Tests;

/// <summary>
/// What the Flash Video writer says about a file, and that a remux through it keeps what the reader
/// found rather than dropping it.
/// </summary>
/// <remarks>
/// This is the case a demux/mux pair is easiest to get wrong in: both halves work, both are tested,
/// and the field that crossed one and not the other is invisible in either test on its own. Before
/// the <c>onMetaData</c> tag existed here, an FLV read and written straight back came out with its
/// title, author, album, encoder and every comment gone, and nothing said so.
/// </remarks>
[TestFixture]
public class FlvWriterTests {

  private static MediaStreamInfo _VideoStream() => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("FLV1"),
    Width = 16,
    Height = 16,
    TimeBase = new Rational(1, 1000),
  };

  private static readonly CodedPacket[] _Packets = [
    new(0, new byte[] { 1, 2, 3, 4 }, PresentationTimestamp: 0, DecodeTimestamp: 0, IsKeyFrame: true),
    new(0, new byte[] { 5, 6, 7, 8 }, PresentationTimestamp: 40, DecodeTimestamp: 40),
  ];

  private static VideoMetadata _Rich() => new() {
    Title = "A film with a name",
    Artist = "Somebody",
    Album = "A series",
    EncodedBy = "the writer under test",
    CreationTime = new DateTimeOffset(2019, 4, 2, 11, 30, 0, TimeSpan.Zero),
    Duration = TimeSpan.FromSeconds(12.5),
    TextEntries = [new("Comment", "a note"), new("Copyright", "nobody"), new("Director", "someone else")],
  };

  [Test]
  [Category("Unit")]
  public void ARemuxKeepsWhatTheReaderFound() {
    var written = VideoIO.Mux<FlvWriter>([_VideoStream()], _Packets, _Rich());

    var read = VideoFormatRegistry.ReadMetadata(written);

    Assert.Multiple(() => {
      Assert.That(read.Title, Is.EqualTo("A film with a name"));
      Assert.That(read.Artist, Is.EqualTo("Somebody"));
      Assert.That(read.Album, Is.EqualTo("A series"));
      Assert.That(read.EncodedBy, Is.EqualTo("the writer under test"));
      Assert.That(read.CreationTime, Is.EqualTo(new DateTimeOffset(2019, 4, 2, 11, 30, 0, TimeSpan.Zero)));
      Assert.That(read.Duration, Is.EqualTo(TimeSpan.FromSeconds(12.5)));
      Assert.That(read.TextEntries.Select(e => (e.Keyword, e.Text)),
        Is.EquivalentTo(new[] { ("Comment", "a note"), ("Copyright", "nobody"), ("Director", "someone else") }));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFilmItselfIsUnchangedByTheAnnouncement() {
    var announced = VideoIO.Mux<FlvWriter>([_VideoStream()], _Packets, _Rich());
    var silent = VideoIO.Mux<FlvWriter>([_VideoStream()], _Packets, VideoMetadata.Empty);

    var fromAnnounced = VideoFormatRegistry.ReadPackets(announced).ToList();
    var fromSilent = VideoFormatRegistry.ReadPackets(silent).ToList();

    Assert.That(fromAnnounced, Has.Count.EqualTo(fromSilent.Count));
    for (var i = 0; i < fromAnnounced.Count; ++i)
      Assert.That(fromAnnounced[i].Data.ToArray(), Is.EqualTo(fromSilent[i].Data.ToArray()).AsCollection, $"packet {i}");
  }

  [Test]
  [Category("Unit")]
  public void MetadataWithNothingToSayWritesNoScriptTagAtAll() {
    var silent = VideoIO.Mux<FlvWriter>([_VideoStream()], _Packets, VideoMetadata.Empty);

    // Tag type 18 is the script tag; a file with nothing to announce should look like one written
    // before anybody announced anything.
    Assert.That(_TagTypes(silent), Has.No.Member((byte)18));
    Assert.That(_TagTypes(VideoIO.Mux<FlvWriter>([_VideoStream()], _Packets, _Rich())), Has.Member((byte)18));
  }

  [Test]
  [Category("Unit")]
  public void AnAnnotationFiledUnderAMeasurementsNameIsDroppedRatherThanWrittenAsOne() {
    var metadata = new VideoMetadata {
      Duration = TimeSpan.FromSeconds(4),
      TextEntries = [new("duration", "not a number at all"), new("Comment", "kept")],
    };

    var read = VideoFormatRegistry.ReadMetadata(VideoIO.Mux<FlvWriter>([_VideoStream()], _Packets, metadata));

    Assert.Multiple(() => {
      Assert.That(read.Duration, Is.EqualTo(TimeSpan.FromSeconds(4)));
      Assert.That(read.TextEntries.Select(e => e.Keyword), Is.EqualTo(new[] { "Comment" }).AsCollection);
    });
  }

  private static List<byte> _TagTypes(byte[] flv) {
    var types = new List<byte>();
    var at = 9 + 4;
    while (at + 11 <= flv.Length) {
      types.Add(flv[at]);
      var size = (flv[at + 1] << 16) | (flv[at + 2] << 8) | flv[at + 3];
      at += 11 + size + 4;
    }

    return types;
  }
}
