namespace Kanawanagasaki.UdpHolePuncher.Test;

using Kanawanagasaki.UdpHolePuncher.Contracts;
using System.Linq;
using System.Security.Cryptography;
using Xunit.Abstractions;

public class SerializationTest(ITestOutputHelper _output)
{
    [Theory]
    [InlineData(null)]
    [InlineData("HelloI'mAPasswordYay")]
    public void ConnectOpTest(string? password)
    {
        var connectOp = new ConnectOp
        {
            Uuid = Guid.NewGuid(),
            Password = password
        };

        var size = connectOp.GetSerializedSize();
        var bytes = new byte[size];
        connectOp.Serialize(bytes);

        var newConnectOp = ConnectOp.Deserialize(bytes);

        Assert.Equal(connectOp.Uuid, newConnectOp.Uuid);
        Assert.Equal(connectOp.Password, newConnectOp.Password);
    }

    [Theory]
    [InlineData(16, 12)]
    [InlineData(16, 16)]
    [InlineData(16, 24)]
    [InlineData(32, 12)]
    [InlineData(32, 16)]
    [InlineData(32, 24)]
    public void HandshakeOpTest(int keyLen, int ivLen)
    {
        var handshakeOp = new HandshakeOp
        {
            AesKey = RandomNumberGenerator.GetBytes(keyLen),
            AesIV = RandomNumberGenerator.GetBytes(ivLen)
        };

        var size = handshakeOp.GetSerializedSize();
        var bytes = new byte[size];
        handshakeOp.Serialize(bytes);

        var newHandshakeOp = HandshakeOp.Deserialize(bytes);

        Assert.Equal(handshakeOp.AesKey, newHandshakeOp.AesKey);
        Assert.Equal(handshakeOp.AesIV, newHandshakeOp.AesIV);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 1000, "hello", null, null, null, null, 16, 12)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 1000, "world", null, null, null, null, 16, 16)]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 10, "foo", "name", null, null, null, 16, 24)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 50, "bar", null, "password", null, null, 32, 12)]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 10, "baz", null, null, new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, null, 32, 16)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 32000, "qwe", null, null, null, new string[] { "AYAYA", "UWU" }, 32, 24)]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 10, "asd", "zxc", "123", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, new string[] { "Hello", "World" }, 64, 12)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 10, "asd", "zxc", "123", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, new string[] { "Hello", "World" }, 64, 32)]
    public void P2POpTest(byte[] ip, ushort port, string project, string? name, string? password, byte[]? extra, string[]? tags, int keyLen, int ivLen)
    {
        var p2pOp = new P2POp
        {
            RemoteClient = new()
            {
                IpBytes = ip,
                Port = port,
                Project = project,
                Name = name,
                Password = password,
                Extra = extra,
                Tags = tags
            },
            AesKey = RandomNumberGenerator.GetBytes(keyLen),
            AesIV = RandomNumberGenerator.GetBytes(ivLen)
        };

        var size = p2pOp.GetSerializedSize();
        var bytes = new byte[size];
        p2pOp.Serialize(bytes);

        var newP2pOp = P2POp.Deserialize(bytes);

        AssertPrivateRemoteClients(p2pOp.RemoteClient, newP2pOp.RemoteClient);
        Assert.Equal(p2pOp.AesKey, newP2pOp.AesKey);
        Assert.Equal(p2pOp.AesIV, newP2pOp.AesIV);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 1000, "hello", null, null, null, null)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 1000, "world", null, null, null, null)]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 10, "foo", "name", null, null, null)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 50, "bar", null, "password", null, null)]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 10, "baz", null, null, new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, null)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 32000, "qwe", null, null, null, new string[] { "AYAYA", "UWU" })]
    [InlineData(new byte[] { 1, 2, 3, 4 }, 10, "asd", "zxc", "123", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, new string[] { "Hello", "World" })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 10, "asd", "zxc", "123", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, new string[] { "Hello", "World" })]
    public void PrivateNetworkClientTest(byte[] ip, ushort port, string project, string? name, string? password, byte[]? extra, string[]? tags)
    {
        var client = new RemoteClient
        {
            IpBytes = ip,
            Port = port,
            Project = project,
            Name = name,
            Password = password,
            Extra = extra,
            Tags = tags
        };

        var size = client.GetSerializedSize();
        var bytes = new byte[size];
        client.Serialize(bytes);

        var newClient = RemoteClient.Deserialize(bytes);

        AssertPrivateRemoteClients(client, newClient);
    }

    [Theory]
    [InlineData("hello", null, null)]
    [InlineData("world", "name", null)]
    [InlineData("foo", null, new[] { "tag" })]
    [InlineData("bar", "New name", new[] { "AYAYA", "UWU", "OWO" })]
    public void PublicNetworkClientTest(string project, string? name, string[]? tags)
    {
        var client = new RemoteClientMin
        {
            Project = project,
            Name = name,
            Tags = tags
        };

        var size = client.GetSerializedSize();
        var bytes = new byte[size];
        client.Serialize(bytes);

        var newClient = RemoteClientMin.Deserialize(bytes);

        AssertPublicRemoteClients(client, newClient);
    }

    [Theory]
    [InlineData(true, "hello", null, null, null, null)]
    [InlineData(false, "world", null, null, null, null)]
    [InlineData(true, "foo", "name", null, null, null)]
    [InlineData(false, "bar", null, "password", null, null)]
    [InlineData(true, "baz", null, null, new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, null)]
    [InlineData(false, "qwe", null, null, null, new string[] { "AYAYA", "UWU" })]
    [InlineData(true, "asd", "zxc", "123", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, new string[] { "Hello", "World" })]
    public void PunchOpTest(bool isQuerable, string project, string? name, string? password, byte[]? extra, string[]? tags)
    {
        var punchOp = new PunchOp
        {
            IsQuerable = isQuerable,
            Project = project,
            Name = name,
            Password = password,
            Extra = extra,
            Tags = tags
        };

        var size = punchOp.GetSerializedSize();
        var bytes = new byte[size];
        punchOp.Serialize(bytes);

        var newPunchOp = PunchOp.Deserialize(bytes);

        Assert.Equal(punchOp.IsQuerable, newPunchOp.IsQuerable);
        Assert.Equal(punchOp.Project, newPunchOp.Project);
        Assert.Equal(punchOp.Name, newPunchOp.Name);
        Assert.Equal(punchOp.Password, newPunchOp.Password);
        Assert.Equal(punchOp.Extra, newPunchOp.Extra);
        Assert.Equal(punchOp.Tags, newPunchOp.Tags);
    }

    [Theory]
    [InlineData("hello", null)]
    [InlineData("world", new string[] { "Hello", "World" })]
    [InlineData("123", new string[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" })]
    public void QueryOpTest(string project, string[]? tags)
    {
        var queryOp = new QueryOp
        {
            Project = project,
            Tags = tags
        };

        var size = queryOp.GetSerializedSize();
        var bytes = new byte[size];
        queryOp.Serialize(bytes);

        var newQueryOp = QueryOp.Deserialize(bytes);

        Assert.Equal(queryOp.Project, newQueryOp.Project);
        Assert.Equal(queryOp.Tags, newQueryOp.Tags);
    }

    [Fact]
    public void QueryResTest()
    {
        var queryRes = new QueryRes
        {
            PrivateClients = Enumerable.Range(1, Random.Shared.Next(1, 10)).Select(x => new RemoteClientMin
            {
                Project = string.Join("", Enumerable.Repeat("a", x)),
                Name = Random.Shared.NextDouble() < 0.5 ? null : "name",
                Tags = Random.Shared.NextDouble() < 0.5 ? null : ["Hello", "World"]
            }).ToArray(),
            PublicClients = Enumerable.Range(1, Random.Shared.Next(1, 10)).Select(x => new RemoteClient
            {
                IpBytes = Random.Shared.NextDouble() < 0.5 ? RandomNumberGenerator.GetBytes(4) : RandomNumberGenerator.GetBytes(16),
                Port = (ushort)Random.Shared.Next(100, 0xFFFF),
                Project = string.Join("", Enumerable.Repeat("a", x)),
                Name = Random.Shared.NextDouble() < 0.5 ? null : "name",
                Password = Random.Shared.NextDouble() < 0.5 ? null : "password",
                Extra = RandomNumberGenerator.GetBytes(Random.Shared.Next(1, 100)),
                Tags = Random.Shared.NextDouble() < 0.5 ? null : ["Hello", "World"]
            }).ToArray()
        };

        var size = queryRes.GetSerializedSize();
        var bytes = new byte[size];
        queryRes.Serialize(bytes);

        var newQueryRes = QueryRes.Deserialize(bytes);

        Assert.Equal(queryRes.PrivateClients.Length, newQueryRes.PrivateClients.Length);
        foreach (var (one, two) in queryRes.PrivateClients.Zip(newQueryRes.PrivateClients))
            AssertPublicRemoteClients(one, two);

        Assert.Equal(queryRes.PublicClients.Length, newQueryRes.PublicClients.Length);
        foreach (var (one, two) in queryRes.PublicClients.Zip(newQueryRes.PublicClients))
            AssertPrivateRemoteClients(one, two);

        _output.WriteLine($"PrivateClients.Length: {queryRes.PrivateClients.Length}, PublicClients.Length: {queryRes.PublicClients.Length}, bytes size: {bytes.Length}");
    }

    private void AssertPrivateRemoteClients(RemoteClient one, RemoteClient two)
    {
        Assert.Equal(one.Uuid, two.Uuid);
        Assert.Equal(one.IpBytes, two.IpBytes);
        Assert.Equal(one.Port, two.Port);
        Assert.Equal(one.Project, two.Project);
        Assert.Equal(one.Name, two.Name);
        Assert.Equal(one.Password, two.Password);
        Assert.Equal(one.Extra, two.Extra);
        Assert.Equal(one.Tags, two.Tags);
    }

    private void AssertPublicRemoteClients(RemoteClientMin one, RemoteClientMin two)
    {
        Assert.Equal(one.Uuid, two.Uuid);
        Assert.Equal(one.Project, two.Project);
        Assert.Equal(one.Name, two.Name);
        Assert.Equal(one.Tags, two.Tags);
    }
}