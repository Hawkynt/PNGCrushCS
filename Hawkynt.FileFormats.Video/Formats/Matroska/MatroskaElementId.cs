namespace FileFormat.Matroska;

/// <summary>
/// The identifiers of the Matroska elements this reader knows, as the bytes sit in the file.
/// </summary>
/// <remarks>
/// Every one of them keeps its length marker, because that is what an EBML identifier is — the
/// stored bytes are the key, so <c>Cluster</c> is <c>0x1F43B675</c> and not the seven bits under the
/// marker. Comparing against the stripped value would match nothing in any file.
/// <para/>
/// This is a fraction of the schema on purpose. A demuxer needs to know where the packets are, which
/// streams there are and what the file says about itself; the rest of Matroska — chapters, cue
/// points, the colour description, the seek head — is walked past like any other unknown element,
/// and naming those here would suggest they were read.
/// </remarks>
internal static class MatroskaElementId {

  // -------- Document --------

  internal const uint EBML_HEADER = 0x1A45DFA3;
  internal const uint DOC_TYPE = 0x4282;
  internal const uint SEGMENT = 0x18538067;

  // -------- The segment's own level --------

  internal const uint SEEK_HEAD = 0x114D9B74;
  internal const uint INFO = 0x1549A966;
  internal const uint TRACKS = 0x1654AE6B;
  internal const uint CLUSTER = 0x1F43B675;
  internal const uint CUES = 0x1C53BB6B;
  internal const uint ATTACHMENTS = 0x1941A469;
  internal const uint CHAPTERS = 0x1043A770;
  internal const uint TAGS = 0x1254C367;

  // -------- Info --------

  internal const uint TIMESTAMP_SCALE = 0x2AD7B1;
  internal const uint DURATION = 0x4489;
  internal const uint MUXING_APP = 0x4D80;
  internal const uint WRITING_APP = 0x5741;
  internal const uint TITLE = 0x7BA9;
  internal const uint DATE_UTC = 0x4461;

  // -------- Tracks --------

  internal const uint TRACK_ENTRY = 0xAE;
  internal const uint TRACK_NUMBER = 0xD7;
  internal const uint TRACK_TYPE = 0x83;
  internal const uint CODEC_ID = 0x86;
  internal const uint CODEC_PRIVATE = 0x63A2;
  internal const uint CODEC_DELAY = 0x56AA;
  internal const uint LANGUAGE = 0x22B59C;
  internal const uint LANGUAGE_BCP47 = 0x22B59D;
  internal const uint TRACK_NAME = 0x536E;
  internal const uint DEFAULT_DURATION = 0x23E383;
  internal const uint VIDEO = 0xE0;
  internal const uint PIXEL_WIDTH = 0xB0;
  internal const uint PIXEL_HEIGHT = 0xBA;
  internal const uint CONTENT_ENCODINGS = 0x6D80;
  internal const uint CONTENT_ENCODING = 0x6240;
  internal const uint CONTENT_ENCODING_TYPE = 0x5033;
  internal const uint CONTENT_COMPRESSION = 0x5034;
  internal const uint CONTENT_COMP_ALGO = 0x4254;
  internal const uint CONTENT_ENCRYPTION = 0x5035;

  // -------- Cluster --------

  internal const uint CLUSTER_TIMESTAMP = 0xE7;
  internal const uint SIMPLE_BLOCK = 0xA3;
  internal const uint BLOCK_GROUP = 0xA0;
  internal const uint BLOCK = 0xA1;
  internal const uint BLOCK_DURATION = 0x9B;
  internal const uint REFERENCE_BLOCK = 0xFB;

  // -------- Attachments --------

  internal const uint ATTACHED_FILE = 0x61A7;
  internal const uint FILE_DESCRIPTION = 0x467E;
  internal const uint FILE_NAME = 0x466E;
  internal const uint FILE_MIME_TYPE = 0x4660;
  internal const uint FILE_DATA = 0x465C;

  // -------- Tags --------

  internal const uint TAG = 0x7373;
  internal const uint TAG_TARGETS = 0x63C0;
  internal const uint TAG_TRACK_UID = 0x63C5;
  internal const uint SIMPLE_TAG = 0x67C8;
  internal const uint TAG_NAME = 0x45A3;
  internal const uint TAG_STRING = 0x4487;

  /// <summary>
  /// Whether an identifier belongs to the segment's own level, which is what ends an element the
  /// file stated no length for.
  /// </summary>
  /// <remarks>
  /// A <c>Cluster</c> written to a pipe carries no length, and where it stops is where the next
  /// element that cannot be inside it starts. These are those elements: the next cluster, and every
  /// other child a segment has. Nothing at this level ever appears inside a cluster, so the first one
  /// encountered is the end of it.
  /// </remarks>
  internal static bool IsSegmentLevel(uint id)
    => id is SEEK_HEAD or INFO or TRACKS or CLUSTER or CUES or ATTACHMENTS or CHAPTERS or TAGS;
}
