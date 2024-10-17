namespace Kanawanagasaki.UdpHolePuncher.Contracts;

public interface ISerializable
{
    int GetSerializedSize();
    void Serialize(Span<byte> span);
}
