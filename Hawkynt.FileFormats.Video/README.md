# Hawkynt.FileFormats.Video

> Pure-C# video containers and codecs, with demuxing, decoding, encoding and muxing kept as four
> separate things.

The video sibling of [`Hawkynt.FileFormats.Images`](https://www.nuget.org/packages/Hawkynt.FileFormats.Images).
A decoded frame is a `FileFormat.Core.RawImage` — the same type every image format in that package
reads and writes — so saving a frame, resampling it, quantising it or comparing it uses the code that
already does those things to a photograph.

## The four contracts

| Contract | Interface | Knows about |
| --- | --- | --- |
| Demux | `IVideoContainerReader<T>` | where the packets are; nothing about what is in them |
| Decode | `IVideoCodecDecoder<T>` | one codec; nothing about which container it came from |
| Encode | `IVideoCodecEncoder<T>` | one codec; nothing about where its packets will be written |
| Mux | `IVideoContainerWriter<T>` | where to put packets; nothing about how they were made |

A container reader hands out `CodedPacket`s and never a picture. That is what makes remuxing
possible at all: reading one container and writing another is a demuxer and a muxer with nothing in
between, and the pictures come out the far side bit for bit. A reader that decoded as it demuxed
could only ever produce pictures, which turns every remux into a re-encode.

Packets and frames are reached lazily. A film is not a list of pictures held in memory, and a caller
who wants one frame of a two-hour recording pays for one frame.

## Supported

| Container | Extensions | Read | Write |
| --- | --- | --- | --- |
| AVI (RIFF) | `.avi` | Y | — |
| Flash Video (FLV) | `.flv`, `.f4v` | Y | — |
| ISO base media (MP4, QuickTime, 3GP) | `.mp4`, `.m4v`, `.mov`, `.qt`, `.3gp`, `.3g2`, `.m4a` | Y | — |
| Matroska / WebM (EBML) | `.mkv`, `.mka`, `.mks`, `.mk3d`, `.webm` | Y | — |
| H.264 byte stream (Annex B) | `.264`, `.h264`, `.avc`, `.x264` | Y | — |
| MPEG program stream (MPEG-1, MPEG-2, VOB) | `.mpg`, `.mpeg`, `.vob`, `.m2p`, `.m2ps` | Y | — |
| Motion JPEG stream | `.mjpg`, `.mjpeg` | Y | — |
| MPEG video elementary stream | `.m1v`, `.m2v`, `.mpv`, `.mpeg1video`, `.mpeg2video` | Y | — |
| MPEG-2 transport stream (also Blu-ray, AVCHD) | `.ts`, `.m2ts`, `.mts`, `.m2t`, `.tsv` | Y | — |

| Codec | Tag | Decode | Encode |
| --- | --- | --- | --- |
| Uncompressed (`BI_RGB`) | 0 | Y | — |
| Motion JPEG | `MJPG`, `mjpg`, `jpeg`, `V_MJPEG` | Y | — |
| MPEG-1 video (ISO/IEC 11172-2) | `MPG1`, `PIM1`, `mp1v` | Y | — |
| MPEG-2 video (ISO/IEC 13818-2) | `MPG2`, `MPEG`, `mp2v`, `m2v1`, `hdv1`–`hdv3`, `V_MPEG2` | Y | — |
| Microsoft RLE | `MRLE`, `mrle`, `BI_RLE8` (1), `BI_RLE4` (2) | Y | — |
| Microsoft Video 1 | `CRAM`, `MSVC`, `WHAM` | Y | — |
| Cinepak | `cvid`, `CVID` | Y | — |
| QuickTime Animation (RLE) | `rle ` | Y | — |
| H.263 (ITU-T H.263 baseline) | `H263`, `s263`, `U263` | Y | — |
| Sorenson Spark (Flash Video's H.263) | `FLV1` | Y | — |
| H.264 / AVC, Baseline I and P slices | `avc1`, `avc3`, `H264`, `X264`, `DAVC`, `VSSH`, `V_MPEG4/ISO/AVC` | Y | — |
| VP8 (RFC 6386) | `VP80`, `vp08`, `V_VP8` | Y | — |
| HuffYUV / FFVHUFF | `HFYU`, `FFVH` | Y | — |

One reader for MP4, MOV, M4V and 3GP because they are one format under four names — the same box
structure with different brands in `ftyp`. Its packet boundaries are not in the data at all: `mdat`
is an undivided heap of bytes, and where each packet starts and stops is a computation over five
tables in `stbl`, which is why a file whose `moov` follows its `mdat` needs no second pass. A
fragmented file, whose sample tables live in `moof` boxes instead, is refused by name rather than
read as a film of no packets.

An MPEG program stream states its packet boundaries even less than that. It chops each elementary
stream into PES packets sized to fill 2048-byte packs, so a picture routinely spans two of them and
one of them routinely holds seven pictures. The payloads are stitched back together and cut again
where the elementary stream itself says a picture starts, which makes a packet the coded picture a
decoder wants with nothing of the container left in it — measured against `ffprobe -fflags +nofillin`
on eleven files, agreeing on every packet's size and timestamp, and reproducing ffmpeg's own extracted
elementary stream byte for byte. Its two systems standards, ISO/IEC 11172-1 and ISO/IEC 13818-1, share
one reader and one start code and no layout at all; which of them a file is, is read off its first
pack header, because a program stream has no version field, no index and no header listing its
streams.

One reader for Matroska and WebM for the reason MP4 and MOV share one: WebM is a Matroska document
with a `DocType` that says so and a shorter list of codecs allowed inside it, and which codecs are
allowed is the business of whoever is asked for a decoder. Its packet boundaries are in neither a
table nor a chunk header but in the clusters themselves, and a single block may carry several frames
at once — all four lacings are unpacked, so a laced block comes out as the packets it holds rather
than as one. Elements the writer stated no length for, which is what a stream written live produces,
end where the next element that cannot be inside them begins.

Matroska names its codecs with strings rather than four-character codes, so a stream from it carries
a `CodecId` and no tag — the exception being `V_MS/VFW/FOURCC`, whose `BITMAPINFOHEADER` holds a real
code. A stream whose blocks were compressed, header-stripped or encrypted before being written is
refused by name rather than handed on as frames it is not.

FLV is the one container here that declares nothing. Its nine-byte header says whether sound and
pictures are present and stops, so the streams are discovered from the tags and each codec from the
first tag belonging to it — which also means the streams are numbered in the order their first tag
appears, as ffprobe numbers them. Two payload shapes are not frames and never become packets: an AVC
sequence header, whose configuration record becomes the stream's private data, and an AAC one, which
does the same.

A transport stream is the one container here that was not designed for a file. It is a broadcast, so
there is no index, no directory and no header: the streams are found by reading the tables the
multiplex repeats — the program association table at PID 0, then a program map per program — and a
coded unit is reassembled out of the 188-byte packets it was cut into, which is also why its packets
are copies rather than windows onto the file. Blu-ray and AVCHD put a four-byte arrival timecode in
front of every packet, so the stride is 192 rather than 188; which of the two a file uses is measured
from its sync bytes rather than taken from its name. A lost packet is caught by the continuity
counter and refused by name, because a unit assembled across one is a frame with a hole in it.

A stream coded with anything else is refused by name — the code, or the container's own name for the
codec where it has one — rather than half decoded into noise.

### MPEG-1 video

I, P and B pictures, with the full block layer: the Annex B variable-length codes, dequantisation
against the default and any loaded quantiser matrices, the inverse transform, and motion compensation
at half-pixel resolution in both directions. Frames come out in display order, so an anchor is held
until the next one arrives and `Flush` is not empty at the end of a stream.

Thirty-one encoded streams were compared with ffmpeg's decode of the same bitstream, plane by plane
and frame by frame: sixteen match byte for byte against ffmpeg's floating-point inverse transform and
the rest differ in at most thirty-two samples of one frame, by one level, without growing across a
group of pictures. That residual is the transform's, which ISO/IEC 11172-2 specifies as a formula
with an accuracy bound rather than as an algorithm; ffmpeg's own two transforms differ from each other
by more.

What is not implemented refuses and says so: D pictures, and a picture size that changes while
pictures predicted from the old one are still held.

### MPEG-2 video

One decoder reads both standards, because ISO/IEC 13818-2 is written that way — it requires a decoder
of itself to decode ISO/IEC 11172-2 as well, and the picture, slice, macroblock and block layers are
the same walk with more fields in them. Which standard a stream is decides itself, from the sequence
extension after the sequence header, and not from what a container called the codec. The two decoder
types exist to claim different four-character codes; behind them is one engine.

What MPEG-2 adds and this reads: the sequence and picture coding extensions; 4:2:0 with MPEG-2's own
chrominance siting, and 4:2:2; `intra_dc_precision`, so a picture may code its DC to nine, ten or
eleven bits; the non-linear quantiser scale; the alternate scan; the second intra coefficient table
(Table B.15); loadable chrominance quantiser matrices; concealment motion vectors; 13818-2's own
dequantisation, which corrects each block's parity once at the end rather than forcing every
coefficient odd; and interlaced coding within a frame picture — field DCT, and field-based motion
compensation where the two fields of a macroblock are predicted separately from either field of the
reference.

What it refuses, by name and with the clause: field pictures, dual-prime prediction, 4:4:4, and the
three scalability extensions.

Thirty-seven encoded streams, eleven hundred frames in all, were compared with ffmpeg's decode of the
same bitstreams — every frame, every sample. Progressive and interlaced; 4:2:0 and 4:2:2; 64×48 up to
704×480; sizes that are and are not whole macroblocks in either direction; greyscale, so that no
chrominance convention could mask a luminance error; every intra DC precision; the alternate scan, the
non-linear quantiser and the second intra table, separately and together; and the same video through
an elementary stream, a program stream and a transport stream. Every one produced the frame count
ffprobe counts.

Against ffmpeg's floating-point inverse transform, twenty-seven of the thirty-seven are identical
sample for sample on every frame. The other ten differ in at most thirteen samples of one frame — out
of a million — by at most three levels, flat across a group of pictures rather than growing, which is
what separates a rounding difference from a fault in prediction or dequantisation. For scale: on those
same streams ffmpeg's own two inverse transforms differ from each other by tens of thousands of samples
per frame. The residual is the transform's, which both standards specify as a formula with an accuracy
bound rather than as an algorithm, and not a disagreement about the bitstream.

### Microsoft RLE

Run-length coded palettised frames at four bits a pixel and at eight, with the end-of-line, delta and
end-of-bitmap escapes. There is no second copy of the coding here: a run-length Windows bitmap is the
same opcodes over the same kind of picture, so the walk lives with the bitmap reader and takes the
canvas it paints on as an argument. That argument is the whole difference between the two uses — a
still starts on an empty canvas, and a frame starts on the frame before it, which is what turns the
escapes from a way of leaving parts of a picture unstated into the entire inter-frame coding.

The coding is lossless, so there is nothing to round: every frame of every stream measured came out
identical to ffmpeg's decode of the same file, key frames and delta frames alike, with no differing
samples at all.

What is not implemented refuses and says so: a depth the coding is not defined at, a depth that
disagrees with the compression stated beside it, a stream carrying no palette, rows stored top-down,
and any opcode that runs off the picture or off the end of the data. QuickTime's `WRLE` — the same
coding with a QuickTime colour table in place of the bitmap header's palette — is not claimed, so a
`.mov` carrying it is refused by name rather than decoded against the wrong colours.
### Microsoft Video 1

Vector quantisation over 4x4 blocks: a block is one colour, two colours chosen per pixel by a
sixteen-bit mask, or eight — the block split into four 2x2 quads with two colours each, chosen by the
same mask. A fourth code skips a run of blocks, and that is the whole of the inter-frame coding: a
skipped block is one that did not change, so the frame before has to still be there to be left alone.
Blocks run bottom to top as a bitmap's rows do, and left to right within a row.

Both depths are one decoder because they are one algorithm. What differs is how wide a colour is and,
at sixteen bits, that the choice between two colours and eight is made by the spare top bit of the
first colour rather than by the second flag byte.

The quantisation is the encoder's, so a decoder reading the same bitstream has nothing to round.
Eleven streams were compared with ffmpeg frame by frame — 4x4 up to 320x240, fifty frames of moving
content, noise, colour bars, in AVI and in Matroska — and every frame of every one is identical,
sample for sample. The eight-bit variant is measured the same way against hand-built streams, since
ffmpeg's own encoder writes only the sixteen-bit one.

Colours are 5-5-5 with red in the high bits, which is what ffprobe calls the codec's pixel format and
what decoding a frame each way and comparing with ffmpeg settles; the format description on
multimedia.cx names the channels the other way round. Five bits are widened to eight by repeating
the pattern rather than shifting, the same rule this library's bitmap reader arrived at against the
same tool.

What is not implemented refuses and says so: a depth other than eight or sixteen, a picture whose
sides are not whole blocks, an eight-bit stream with no palette, a skip run reaching past the last
block, a frame that stops before every block is accounted for, and an opcode wanting more bytes than
the packet holds. A skip run of *no* blocks is refused too, and for a different reason: read as the
format describes it the run is a no-op, where ffmpeg abandons the rest of the frame at one. Both
readings produce a picture, they differ across everything after the run, and nothing in the file says
which was meant.
### Cinepak

Vector quantisation with two codebooks per strip. A codebook entry is four luminance samples and one
chrominance pair — a 4x4 block at 12 bits a pixel — and a block is coded either as one entry, whose
four samples are each stretched over a 2x2 square, or as four, one per quadrant. One byte a block or
four, and everything else is in the codebooks. Both codebook depths are read, the 12-bit one and the
8-bit grey one.

The inter-frame coding is in two places at once, which is what makes the format small: a vector list
may say a block is unchanged and code nothing for it, and a codebook chunk may restate a handful of
entries and leave the other two hundred. So both the picture and the codebooks carry over between
frames, and a strip that states nothing is not a strip of nothing.

Fifteen streams were compared with ffmpeg frame by frame — 4x4 up to 640x480, a hundred frames of
zooming fractal, noise, greyscale, one strip forced and eight, in AVI and in QuickTime — and every
one of 303 frames is identical, sample for sample. Nothing drifts because nothing differs.

Two things measurement decided rather than the documentation. The chrominance bytes are **signed**
and not biased by 128, which the technical note has the other way round; a stream whose codebook
sweeps every value of each byte gives 5120 samples of what the answer must be, and the signed reading
reproduces all 5120 where the biased one reproduces none. And the halving in the green row truncates
toward zero rather than shifting right, which is a different number for a negative odd difference and
wrong in 319 of those same samples.

Every strip after the first states a top of zero and a bottom that is really its height, so it is
placed under the one before it. Read literally, a three-strip frame draws all three strips across the
top third of the picture — a picture rather than an error, and so one that would never be noticed.

What is not implemented refuses and says so: a strip identifier that is neither intra nor inter, a
chunk type the format does not define, a strip reaching outside its frame or not made of whole
blocks, a picture size that changes part way through a stream, a vector list stopping before every
block is accounted for, and any chunk shorter than it says it is.
### QuickTime Animation (RLE)

Lossless, and line-based rather than block-based: a frame names the band of lines it touches and
writes them as runs, literal pixels and skips over a canvas the frames before it left behind. All of
1, 2, 4, 8, 16, 24 and 32 bits, and the greyscale depths 33, 34, 36 and 40 that are the same indices
into a ramp running from white. Thirty-two bits carries alpha and the alpha survives.

Every count in the bitstream is in coded units and not in pixels. Above eight bits a unit is a pixel;
at eight and below it is four bytes — four indices at eight bits, eight at four, sixteen at two — and
at one bit it is two bytes, which is sixteen pixels again. One bit is a different shape altogether:
each opcode carries its own skip, and the skip's top bit is what starts a line.

Twenty-two streams covering every depth ffmpeg's encoder can write were decoded here and by ffmpeg and
compared pixel for pixel on every frame, alpha included: 360 frames, all identical. The depths ffmpeg
cannot encode — one, two and four bits, eight bits through a colour table, and widths that are not a
whole number of coded units — were checked the other way round, by building streams that say a known
picture and confirming ffmpeg reads them as that picture and this reads them as ffmpeg does: another
sixty frames across fifteen streams, all identical.

What refuses: a depth the compressor does not code; an indexed depth carrying no colour table, since
the Macintosh default palettes cannot be checked against anything here and a picture drawn through a
guessed table cannot be told from one drawn through the right one; a stream that opens with a frame
touching only part of the picture; and any count that would write outside the line it is on.
### VP8

The codec WebM was built around, and all of it: the boolean entropy decoder, segmentation, both loop
filters, up to eight token partitions, all fourteen intra prediction modes, prediction from any of
the three reference frames with the six-tap and bilinear sub-pixel filters, and the probability state
that carries from one frame to the next. A frame the stream asks not to be shown — an alternate
reference built from several frames at once — is decoded, kept as a reference, and not handed back.

Fifty-three encoded streams and six built by hand — 3,189 coded frames, 3,116 of them shown — were
compared with ffmpeg's decode of the same bitstreams plane by plane and sample by sample. Every plane
of every frame is identical: not close, not on average, the same bytes. That is the only acceptable
result, because the loss happened in the encoder and both decoders are reading what came out of it —
and because an error in prediction or in the loop filter shows up as a small difference everywhere
that grows with every frame until the next key frame.

The streams cover both filters, sharpness zero to seven, all four bitstream versions, one to eight
token partitions, segmentation, hidden reference frames, motion vector sign bias, every split
partitioning, every subblock mode, every range token, and picture sizes from 16x16 to 1280x720
including sizes that are not a whole number of macroblocks. The reference-buffer copies and the
segment-based filter levels, which no encoder here will emit, were reached with hand-written frame
headers — decoded by ffmpeg as well, so agreement is still the measurement.

What is not implemented refuses and says so: a bitstream version RFC 6386 reserves, a key frame that
sets the reserved colour space or clamping fields, a stream that begins at an interframe, a truncated
packet, and a partition table that does not fit in one.

### H.263 and Sorenson Spark

Baseline ITU-T H.263: the picture, group of blocks, macroblock and block layers, intra and predicted
pictures, one motion vector per macroblock at half-pixel resolution with the median predictor of
clause 6.1.1, and the inverse quantisation of 6.2.1. Group headers are optional in the bitstream and
whether each one was present is remembered, because the prediction rules treat the macroblocks above
a group as unavailable only when that group opened with a header of its own.

Sorenson Spark — what Flash Video calls `FLV1` — is the same codec from the group of blocks layer
down and a different bitstream above it, so it shares everything here but the picture header. Its
three real differences are all in the decoder by name: it states its own picture size rather than
naming one of five formats, it has no group of blocks layer at all, and a stream of version 1 puts a
bit in front of the coefficient escape choosing between a seven-bit and an eleven-bit level. A
Sorenson picture may also be disposable — predicted, shown, and never predicted from — which is kept
rather than ignored, because keeping it as a reference would put every picture after it one
prediction out of step.

Thirty encoded streams, seven hundred and forty-three frames, were compared with ffmpeg's decode of
the same bitstreams **plane by plane** and sample by sample — sizes from 100x60 to 704x576, quantisers
1 to 31, groups of pictures from one frame to fifty, and streams with and without group headers.
Plane by plane and not in RGB, because turning 4:2:0 samples into RGB is a display convention rather
than part of the decode and the two conventions differ: this library interpolates the chrominance
planes back up and ffmpeg repeats each sample across its square, which on a picture of hard colour
edges puts nearly half the samples of every frame tens of levels apart while the decoded samples are
identical. Feeding ffmpeg's own decoded planes through the conversion here reproduces that difference
exactly, with no decoder of ours involved, and the same comparison does the same thing to the MPEG-1
decoder above.
Twenty-one of the thirty match sample for sample on every frame against ffmpeg's floating-point
inverse transform; the rest differ in at most about forty samples of a frame out of thirty-eight
thousand, always by exactly one level, and the difference stays at one level across fifty frames with
no intra picture to reset it. That residual is the transform's, which H.263 Annex A specifies as an
accuracy bound rather than as an algorithm; ffmpeg's own two transforms differ from each other by an
order of magnitude more on the same streams.

What is not implemented refuses and says so, naming the annex and the field: the extended picture
header of clause 5.1.4 and everything it signals, unrestricted motion vectors (Annex D), arithmetic
coding (Annex E), advanced prediction and its four vectors per macroblock (Annex F), PB-frames
(Annex G), continuous presence multipoint (Annex C), and the escape level Annex T reserves.

### H.264 / AVC

I and P slices of the Baseline and Constrained Baseline profiles, and every Main or High profile
stream that happens to be coded without the tools those profiles add. That is: CAVLC, 4:2:0, 8-bit
samples, progressive frames, one slice group, the 4x4 transform and flat quantiser matrices. Within
it, everything — the nine Intra_4x4 modes, Intra_16x16 and chroma prediction, `I_PCM`, all four
macroblock partitionings and all four sub-macroblock partitionings, multiple reference frames with
list reordering, quarter-sample motion with the six-tap luma filter and bilinear chroma, constrained
intra prediction, and the deblocking filter with per-slice offsets and both disable modes.

Both delivery forms, because H.264 has two. A transport stream, a program stream and a bare `.264`
carry NAL units separated by start codes; MP4, Matroska and FLV carry each unit behind its length,
with the parameter sets in an `AVCDecoderConfigurationRecord` in the container's header. Which form a
stream is in is decided from whether that record is present rather than guessed at each packet, and
the same content in either form decodes to identical frames.

Forty-six encoded streams were compared with ffmpeg's decode of the same bitstream, plane by plane
and frame by frame, and **every sample of every frame is identical** — across quantisers 1 to 51,
picture sizes from 16x16 to 640x480 including one whose size is not a whole number of macroblocks and
is therefore cropped, one to eight reference frames, one to nine slices a picture, deblocking off and
at both offset extremes, constant quantiser and two rate-controlled modes, intra refresh, a High
profile stream that uses none of the High profile tools, and the same content through all five
containers. Four of them are a single intra picture followed by 125 to 200 predicted ones, which is
the shape in which a small error compounds: the difference stays at zero for the whole chain. H.264
specifies its inverse transform as exact integer arithmetic rather than as a formula with an accuracy
bound, so exact equality is the right bar here, unlike MPEG-1 above.

Three things no encoder in ordinary use emits are covered by built streams instead, because a
comparison cannot reach what nothing produces: `I_PCM` macroblocks, reference picture list
reordering, and marking a reference unused part way through a sequence.

What is not implemented refuses by name and cites the clause: CABAC, B slices, SP and SI slices, the
8x8 transform and scaling matrices, 4:2:2, 4:4:4 and monochrome, sample depths above eight, field
pictures and MBAFF, flexible macroblock ordering, weighted prediction, long-term references, slice
data partitioning, redundant coded pictures, and the scalable and multiview extensions.

### HuffYUV and FFVHUFF

Lossless and intra only: no transform, no quantiser. A sample is predicted from its neighbours — from
the left, by gradient, or by the median of the two and the plane through them — and the difference is
Huffman coded with one table a plane.

Two things about it are easy to get wrong and both are load-bearing. **The bits are in little-endian
words**, so every four bytes of a frame have to be turned round before any of it decodes; the raw
first pixel arriving as alpha, red, green, blue is that swap showing through. And **the Huffman codes
are handed out from the longest length down**, not the shortest up, so the canonical assignment a
reader reaches for first decodes nothing.

Three header forms, and which one a file uses is not its four-character code — `HFYU` and `FFVH` both
write all three. One states a bitstream depth and codes 4:2:2 groups along each row, or colour a
pixel at a time bottom row first; one states a sample depth with the chroma subsampling packed into
its low nibble and codes each plane through to its end; the third is the original codec, which states
nothing at all and is refused rather than guessed at.

Eighty streams were decoded here and by ffmpeg: every pixel format its two encoders will write, each
with all three predictors, progressive and interlaced, with the tables in the stream description and
in every frame, at sizes from 2x2 to 352x576. The formats that need no colour conversion — `gray`,
`gbrp`, `gbrap`, `rgb24`, `bgra` — are compared against ffmpeg's frames directly and every frame is
identical. The luminance-and-chrominance formats are compared plane by plane against ffmpeg's decoded
planes, and every sample of every plane is identical: 362 frames in all, none differing anywhere.

What refuses: the original codec; samples deeper than eight bits; a description that states neither
interlaced nor progressive and expects the height to be guessed from; a prediction method that is
none of the three; a Huffman table whose lengths do not describe a complete code; and interlaced
4:2:0 with median prediction, whose row order could not be established against any file — reading it
as the nearest arrangement that is known reproduces five rows and then diverges, which is the one
answer a decoder must not give.

## 📜 License

LGPL-3.0-or-later.
