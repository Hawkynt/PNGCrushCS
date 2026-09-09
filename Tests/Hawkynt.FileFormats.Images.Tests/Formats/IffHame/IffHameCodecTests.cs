using FileFormat.IffHame;

namespace FileFormat.IffHame.Tests;

[TestFixture]
public sealed class IffHameCodecTests {

  [Test]
  [Category("Unit")]
  public void Decode_KnownCommandVector_MatchesPublishedHameSemantics() {
    var palette = new byte[IffHameCodec.MaximumPaletteEntries * 3];
    palette[0] = 12;
    palette[1] = 34;
    palette[2] = 56;
    palette[64 * 3] = 100;
    palette[64 * 3 + 1] = 110;
    palette[64 * 3 + 2] = 120;

    byte[] commands = [
      0x00, // bank 0, register 0
      0x7F, // blue = 63 << 2 = 252
      0xA0, // red = 32 << 2 = 128
      0xD0, // green = 16 << 2 = 64
      0x3D, // select bank 1, held colour unchanged
      0x00, // bank 1, register 0
      0x3C, // select bank 0, held colour unchanged
      0x00, // bank 0, register 0
    ];

    var rgb = IffHameCodec.Decode(commands, commands.Length, 1, palette, IffHameCodec.MaximumPaletteEntries);

    Assert.That(rgb, Is.EqualTo(new byte[] {
      12, 34, 56,
      12, 34, 252,
      128, 34, 252,
      128, 64, 252,
      128, 64, 252,
      100, 110, 120,
      100, 110, 120,
      12, 34, 56,
    }));
  }

  [Test]
  [Category("Unit")]
  public void Encode_KnownComponentChanges_EmitHardwareCommandBytes() {
    ReadOnlySpan<byte> rgb = [
      0, 0, 0,
      0, 0, 252,
      128, 0, 252,
      128, 64, 252,
    ];
    ReadOnlySpan<byte> palette = [0, 0, 0];

    var commands = IffHameCodec.Encode(rgb, 4, 1, palette, 1);

    Assert.That(commands, Is.EqualTo(new byte[] { 0x00, 0x7F, 0xA0, 0xD0 }));
  }

  [Test]
  [Category("Unit")]
  public void CarrierPalette_RecoversAllFourRgbIBits() {
    var palette = IffHameCodec.CreateCarrierPalette();

    for (byte nibble = 0; nibble < 16; ++nibble)
      Assert.That(IffHameCodec.CarrierIndexToNibble(nibble, palette), Is.EqualTo(nibble), $"carrier index {nibble}");
  }
}
