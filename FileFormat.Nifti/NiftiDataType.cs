namespace FileFormat.Nifti;

/// <summary>NIfTI data type codes indicating voxel value representation.</summary>
public enum NiftiDataType : short {
  UInt8 = 2,
  Int16 = 4,
  Int32 = 8,
  Float32 = 16,
  Float64 = 64,
  Rgb24 = 128,
  Int8 = 256,
  UInt16 = 512,
  UInt32 = 768
}
