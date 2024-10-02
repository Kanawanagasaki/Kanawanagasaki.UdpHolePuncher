namespace Kanawanagasaki.UdpHolePuncher.Server;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

internal static class Clients
{
    private static readonly ConcurrentDictionary<IPEndPoint, Client> _endpointToClient = new();
    private static readonly Dictionary<string, HashSet<IPEndPoint>> _tokenToEndpoints = new();
    private static object _lock = new object();

    internal static bool IsClientExists(IPEndPoint endpoint)
        => _endpointToClient.ContainsKey(endpoint);

    internal static Client GetClientByEndpoint(IPEndPoint endpoint)
    {
        var client = _endpointToClient.GetValueOrDefault(endpoint);
        if (client is not null)
            return client;

        Console.WriteLine($"@{endpoint}: client connected");

        client = new(endpoint);
        _endpointToClient.AddOrUpdate(endpoint, client, (_, _) => client);
        return client;
    }

    internal static void UpdateClientTime(Client client)
        => client.LastTimeSeen = Stopwatch.GetTimestamp();

    internal static void UpdateClientTime(IPEndPoint endpoint)
        => UpdateClientTime(GetClientByEndpoint(endpoint));

    internal static void RemoveClient(IPEndPoint endpoint)
    {
        if (_endpointToClient.TryRemove(endpoint, out var client))
        {
            RemoveClientFromGroup(endpoint, client.RemoteClient?.Token ?? string.Empty);
            client.Dispose();

            Console.WriteLine($"@{endpoint}: client disconnected");
        }
    }

    internal static IEnumerable<Client>? GetGroupByToken(string token)
    {
        lock (_lock)
        {
            var group = _tokenToEndpoints.GetValueOrDefault(token);
            return group is null ? null : group.Select(GetClientByEndpoint);
        }
    }

    internal static void AddClientToGroup(IPEndPoint endpoint, string token)
    {
        lock (_lock)
        {
            var group = _tokenToEndpoints.GetValueOrDefault(token);
            if (group is null)
            {
                _tokenToEndpoints[token] = group = new();
                Console.WriteLine($"{token}: group created");
            }
            group.Add(endpoint);

            Console.WriteLine($"@{endpoint}: client added to group");
        }
    }

    internal static void RemoveClientFromGroup(IPEndPoint endpoint, string token)
    {
        lock (_lock)
        {
            if (_tokenToEndpoints.TryGetValue(token, out var group))
            {
                group.Remove(endpoint);
                Console.WriteLine($"@{endpoint}: client removed from group");

                if (group.Count == 0)
                {
                    _tokenToEndpoints.Remove(token);
                    Console.WriteLine($"{token}: group deleted");
                }
            }
        }
    }

    internal static void ClearInactiveClients()
    {
        foreach (var client in _endpointToClient.Values)
        {
            if (TimeSpan.FromMinutes(10) < Stopwatch.GetElapsedTime(client.LastTimeSeen))
            {
                RemoveClientFromGroup(client.Endpoint, client.RemoteClient?.Token ?? string.Empty);
                _endpointToClient.TryRemove(client.Endpoint, out _);

                Console.WriteLine($"{client.RemoteClient?.Name}@{client.Endpoint}: client disconnected due to inactivity");

                client.Dispose();
            }
        }
    }

    internal static void ClearEmptyGroups()
    {
        lock (_lock)
        {
            foreach (var (token, endpoints) in _tokenToEndpoints.ToArray())
            {
                foreach (var endpoint in endpoints.ToArray())
                {
                    if (!_endpointToClient.ContainsKey(endpoint))
                    {
                        endpoints.Remove(endpoint);
                        Console.WriteLine($"@{endpoint}: client removed from group");
                    }
                }
                if (endpoints.Count == 0)
                {
                    _tokenToEndpoints.Remove(token);
                    Console.WriteLine($"{token}: group deleted");
                }
            }
        }
    }
}
