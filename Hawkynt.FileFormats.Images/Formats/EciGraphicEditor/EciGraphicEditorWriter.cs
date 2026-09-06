using System;
using System.Buffers.Binary;

namespace FileFormat.EciGraphicEditor;

/// <summary>Assembles ECI Graphic Editor file bytes from an <see cref="EciGraphicEditorFile"/>.</summary>
/// <remarks>
/// The unpacked form only. A .ecp says the same thing through a run-length coder and saves nothing
/// a caller asked for, so what is written is the 32770 bytes the editor itself loads: the address,
/// then each frame's bitmap at the start of its bank and its eight video matrices at 8192 into it.
/// The 192 bytes between the two are address-space padding and are written as zero.
/// </remarks>
public static class EciGraphicEditorWriter {

  public static byte[] ToBytes(EciGraphicEditorFile file) {
    ArgumentNullException.ThrowIfNull(file.FirstBitmap);
    ArgumentNullException.ThrowIfNull(file.FirstScreens);
    ArgumentNullException.ThrowIfNull(file.SecondBitmap);
    ArgumentNullException.ThrowIfNull(file.SecondScreens);

    var result = new byte[EciGraphicEditorFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    var payload = result.AsSpan(EciGraphicEditorFile.LoadAddressSize);
    _Place(file.FirstBitmap, payload, 0, EciGraphicEditorFile.BitmapSize);
    _Place(file.FirstScreens, payload, EciGraphicEditorFile.ScreensOffsetInBank, EciGraphicEditorFile.ScreensSize);
    _Place(file.SecondBitmap, payload, EciGraphicEditorFile.SecondBankOffset, EciGraphicEditorFile.BitmapSize);
    _Place(
      file.SecondScreens, payload,
      EciGraphicEditorFile.SecondBankOffset + EciGraphicEditorFile.ScreensOffsetInBank,
      EciGraphicEditorFile.ScreensSize);

    return result;
  }

  private static void _Place(byte[] section, Span<byte> payload, int offset, int length) =>
    section.AsSpan(0, Math.Min(section.Length, length)).CopyTo(payload[offset..]);
}
