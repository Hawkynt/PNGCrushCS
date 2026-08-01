using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.Psb;

namespace FileFormat.Psb.Tests;

/// <summary>
/// The colour table of an indexed Photoshop file, which is laid out a channel at a time.
/// </summary>
/// <remarks>
/// Photoshop stores the table the way it stores the pixels — 256 reds, then 256 greens, then 256
/// blues — and it is always the full 768 bytes however few entries are in use. Both halves here had
/// it as RGB triplets instead: reading made entry 0 the first three reds and entry 1 the next three,
/// so a four-colour file came back in four shades of red, and writing produced a header claiming an
/// indexed image with no table at all, which ImageMagick refuses as an improper image header.
///
/// The round trip could not see either: one wrote triplets and the other read triplets.
/// </remarks>
[TestFixture]
public sealed class PsbPaletteTests
{
    /// <summary>Red, green, blue and white as Photoshop stores them: all reds, all greens, all blues.</summary>
    private static byte[] PlanarTable()
    {
        var table = new byte[768];
        byte[] reds = [255, 0, 0, 255];
        byte[] greens = [0, 255, 0, 255];
        byte[] blues = [0, 0, 255, 255];
        reds.CopyTo(table, 0);
        greens.CopyTo(table, 256);
        blues.CopyTo(table, 512);
        return table;
    }

    private static PsbFile Indexed(byte[] indices, byte[]? palette) => new()
    {
        Width = 4,
        Height = 1,
        Channels = 1,
        Depth = 8,
        ColorMode = PsbColorMode.Indexed,
        PixelData = indices,
        Palette = palette,
    };

    [Test]
    [Category("Unit")]
    public void Read_ResolvesEachIndexThroughTheChannelBlocks()
    {
        var rgb = PsbFile.ToRawImage(Indexed([0, 1, 2, 3], PlanarTable())).ToRgb24();

        Assert.Multiple(() =>
        {
            Assert.That(rgb[..3], Is.EqualTo(new byte[] { 255, 0, 0 }), "index 0");
            Assert.That(rgb[3..6], Is.EqualTo(new byte[] { 0, 255, 0 }), "index 1");
            Assert.That(rgb[6..9], Is.EqualTo(new byte[] { 0, 0, 255 }), "index 2");
            Assert.That(rgb[9..12], Is.EqualTo(new byte[] { 255, 255, 255 }), "index 3");
        });
    }

    /// <summary>
    /// The section is that size or the file is malformed, however few colours are actually used.
    /// </summary>
    [Test]
    [Category("Unit")]
    public void Write_AlwaysEmitsAFullTable()
    {
        var image = new RawImage
        {
            Width = 4,
            Height = 1,
            Format = PixelFormat.Indexed8,
            PixelData = [0, 1, 2, 3],
            Palette = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255],
            PaletteCount = 4,
        };

        var file = PsbFile.FromRawImage(image);

        Assert.That(file.Palette, Has.Length.EqualTo(768), "a four-colour image still writes 768 bytes");
        Assert.Multiple(() =>
        {
            Assert.That(file.Palette![..4], Is.EqualTo(new byte[] { 255, 0, 0, 255 }), "the reds");
            Assert.That(file.Palette![256..260], Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "the greens");
            Assert.That(file.Palette![512..516], Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "the blues");
        });
    }

    /// <summary>The bytes of the file itself: the header says indexed, so a table has to follow it.</summary>
    [Test]
    [Category("Unit")]
    public void Write_PutsTheTableInTheColourModeSection()
    {
        var image = new RawImage
        {
            Width = 4,
            Height = 1,
            Format = PixelFormat.Indexed8,
            PixelData = [0, 1, 2, 3],
            Palette = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255],
            PaletteCount = 4,
        };

        var bytes = PsbWriter.ToBytes(PsbFile.FromRawImage(image));

        Assert.Multiple(() =>
        {
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(24)), Is.EqualTo(2), "colour mode: indexed");
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(26)), Is.EqualTo(768u), "colour mode data length");
            Assert.That(bytes[30], Is.EqualTo(255), "and the table really is there");
        });
    }

    [Test]
    [Category("Unit")]
    public void RoundTrip_KeepsTheColours()
    {
        var image = new RawImage
        {
            Width = 4,
            Height = 1,
            Format = PixelFormat.Indexed8,
            PixelData = [0, 1, 2, 3],
            Palette = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255],
            PaletteCount = 4,
        };

        var back = PsbFile.ToRawImage(PsbReader.FromBytes(PsbWriter.ToBytes(PsbFile.FromRawImage(image)))).ToRgb24();

        Assert.That(back[..12], Is.EqualTo(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 }));
    }
}
