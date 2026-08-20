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
| ISO base media (MP4, QuickTime, 3GP) | `.mp4`, `.m4v`, `.mov`, `.qt`, `.3gp`, `.3g2`, `.m4a` | Y | — |
| Motion JPEG stream | `.mjpg`, `.mjpeg` | Y | — |

| Codec | Tag | Decode | Encode |
| --- | --- | --- | --- |
| Uncompressed (`BI_RGB`) | 0 | Y | — |
| Motion JPEG | `MJPG`, `mjpg`, `jpeg` | Y | — |

One reader for MP4, MOV, M4V and 3GP because they are one format under four names — the same box
structure with different brands in `ftyp`. Its packet boundaries are not in the data at all: `mdat`
is an undivided heap of bytes, and where each packet starts and stops is a computation over five
tables in `stbl`, which is why a file whose `moov` follows its `mdat` needs no second pass. A
fragmented file, whose sample tables live in `moof` boxes instead, is refused by name rather than
read as a film of no packets.

A stream coded with anything else is refused by name — the four-character code and the stream
handler are both in the message — rather than half decoded into noise.

## 📜 License

LGPL-3.0-or-later.
