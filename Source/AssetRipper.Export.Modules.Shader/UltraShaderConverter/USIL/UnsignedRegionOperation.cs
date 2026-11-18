namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL
{
    // A validated, closed DXBC unsigned slice. Lane references are scalar to
    // make aliasing and in-place conversion explicit.
    public record UnsignedRegionOperation(string Kind, int DestinationRegister, int DestinationLane,
        int LeftRegister = 0, int LeftLane = 0, int RightRegister = 0, int RightLane = 0, uint Index = 0);
}
