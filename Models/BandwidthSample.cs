namespace SshStudio.Models;

public sealed class BandwidthSample
{
    public BandwidthSample(double height)
    {
        Height = Math.Clamp(height, 6, 46);
    }

    public double Height { get; }
}
