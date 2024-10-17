namespace Kanawanagasaki.UdpHolePuncher.Server;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

internal static class Clients
{
    private static readonly ConcurrentDictionary<Guid, Client> _uuidToClient = new();
    private static readonly ConcurrentDictionary<IPEndPoint, Client> _endpointToClient = new();
    private static readonly Dictionary<string, HashSet<IPEndPoint>> _projectToEndpoints = new();
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

    internal static Client? GetClientByUuid(Guid uuid)
        => _uuidToClient.GetValueOrDefault(uuid);

    internal static void UpdateClientTime(Client client)
        => client.LastTimeSeen = Stopwatch.GetTimestamp();

    internal static void UpdateClientTime(IPEndPoint endpoint)
        => UpdateClientTime(GetClientByEndpoint(endpoint));

    internal static void UpdateClientUuid(Client client)
    {
        if (client.RemoteClient is not null)
            _uuidToClient.AddOrUpdate(client.RemoteClient.Uuid, client, (_, _) => client);
    }

    internal static void RemoveClient(IPEndPoint endpoint)
    {
        if (_endpointToClient.TryRemove(endpoint, out var client))
        {
            if (client.RemoteClient is not null)
                _uuidToClient.TryRemove(client.RemoteClient.Uuid, out _);

            RemoveClientFromGroup(endpoint, client.RemoteClient?.Project ?? string.Empty);

            client.Dispose();

            Console.WriteLine($"@{endpoint}: client disconnected");
        }
    }

    internal static IEnumerable<Client>? GetGroupByProject(string project)
    {
        lock (_lock)
        {
            var group = _projectToEndpoints.GetValueOrDefault(project);
            return group is null ? null : group.Select(GetClientByEndpoint);
        }
    }

    internal static void AddClientToGroup(IPEndPoint endpoint, string project)
    {
        lock (_lock)
        {
            var group = _projectToEndpoints.GetValueOrDefault(project);
            if (group is null)
            {
                _projectToEndpoints[project] = group = new();
                Console.WriteLine($"{project}: group created");
            }
            group.Add(endpoint);

            Console.WriteLine($"@{endpoint}: client added to group");
        }
    }

    internal static void RemoveClientFromGroup(IPEndPoint endpoint, string project)
    {
        lock (_lock)
        {
            if (_projectToEndpoints.TryGetValue(project, out var group))
            {
                group.Remove(endpoint);
                Console.WriteLine($"@{endpoint}: client removed from group");

                if (group.Count == 0)
                {
                    _projectToEndpoints.Remove(project);
                    Console.WriteLine($"{project}: group deleted");
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
                if (client.RemoteClient is not null)
                    _uuidToClient.TryRemove(client.RemoteClient.Uuid, out _);

                RemoveClientFromGroup(client.Endpoint, client.RemoteClient?.Project ?? string.Empty);
                _endpointToClient.TryRemove(client.Endpoint, out _);

                Console.WriteLine($"{client.RemoteClient?.Name}@{client.Endpoint}: client disconnected due to inactivity");

                client.Dispose();
            }
        }

        foreach (var (uuid, client) in _uuidToClient)
            if (client.RemoteClient is null || client.RemoteClient.Uuid != uuid || !_endpointToClient.ContainsKey(client.Endpoint))
                _uuidToClient.TryRemove(uuid, out _);
    }

    internal static void ClearEmptyGroups()
    {
        lock (_lock)
        {
            foreach (var (token, endpoints) in _projectToEndpoints.ToArray())
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
                    _projectToEndpoints.Remove(token);
                    Console.WriteLine($"{token}: group deleted");
                }
            }
        }
    }
}
