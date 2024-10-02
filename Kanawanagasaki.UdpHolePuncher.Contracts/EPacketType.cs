namespace Kanawanagasaki.UdpHolePuncher.Contracts;

public enum EPacketType : byte
{
    RSAPublicKey = 1,
    RSAEncryptedAESKey = 2,
    HandshakeComplete = 3,
    AESEncryptedData = 4,
    Ping = 5,
    Pong = 6,
    Disconnect = 127
}
