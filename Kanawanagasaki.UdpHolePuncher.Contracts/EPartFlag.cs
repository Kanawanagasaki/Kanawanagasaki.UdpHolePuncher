namespace Kanawanagasaki.UdpHolePuncher.Contracts;

[Flags]
public enum EPartFlag : byte
{
    None = 0,
    IsIpV6 = 1 << 0,
    HasName = 1 << 1,
    HasPassword = 1 << 2,
    HasExtra = 1 << 3,
    HasPublicExtra = 1 << 4,
    HasTags = 1 << 5,
    IsQuerable = 1 << 6
}
