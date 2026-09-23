using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

// DataContractJsonSerializer supports the existing [[x,y], ...] JSON format.
// JsonUtility does not support these nested arrays.
[DataContract]
public sealed class SplineJsonDocument
{
    [DataMember] public SplineJsonRoad[] roads;
    // Keep the C# API stable; the JSON name is now mainRoad.
    [DataMember(Name = "mainRoad")] public SplineJsonStraight[] straightConnections;
    [DataMember(Name = "straightConnections", EmitDefaultValue = false)] private SplineJsonStraight[] legacyStraightConnections;

    [OnDeserialized]
    private void ReadLegacyMainRoad(StreamingContext context)
    {
        if (straightConnections != null && legacyStraightConnections != null)
            throw new FormatException("Use mainRoad or straightConnections, not both.");
        straightConnections = straightConnections ?? legacyStraightConnections;
        legacyStraightConnections = null;
    }

    public static SplineJsonDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new FormatException("Road JSON is empty.");
        SplineJsonDocument document;
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            document = (SplineJsonDocument)new DataContractJsonSerializer(typeof(SplineJsonDocument)).ReadObject(stream);
        if (document == null || document.roads == null || document.roads.Length == 0)
            throw new FormatException("JSON must contain a non-empty roads array.");
        document.Validate();
        return document;
    }

    private void Validate()
    {
        var lookup = new Dictionary<int, SplineJsonRoad>();
        foreach (var road in roads)
        {
            if (road == null || road.id <= 0 || lookup.ContainsKey(road.id))
                throw new FormatException("Road IDs must be positive and unique.");
            ValidatePoints(road.points, "Road " + road.id);
            if (road.controlPoint != null) ValidatePoint(road.controlPoint, "Road controlPoint");
            road.nodeS = road.nodeS ?? Array.Empty<SplineJsonConnection>();
            road.nodeE = road.nodeE ?? Array.Empty<SplineJsonConnection>();
            lookup.Add(road.id, road);
        }
        straightConnections = straightConnections ?? Array.Empty<SplineJsonStraight>();
        var bridges = new HashSet<string>();
        foreach (var bridge in straightConnections)
        {
            if (bridge == null) throw new FormatException("Null straight connection.");
            ValidatePoints(bridge.points, "Straight connection");
            ValidateEndpoint(lookup, bridge.fromRoadNo, bridge.fromNode);
            ValidateEndpoint(lookup, bridge.toRoadNo, bridge.toNode);
            if (!bridge.bidirectional)
                throw new FormatException("This editor requires bidirectional straight connections.");
            string key = LinkKey(bridge.fromRoadNo, bridge.fromNode, bridge.toRoadNo, bridge.toNode);
            if (!bridges.Add(key)) throw new FormatException("Duplicate straight connection: " + key);
            var from = lookup[bridge.fromRoadNo];
            var to = lookup[bridge.toRoadNo];
            if (!Contains(from.Connections(bridge.fromNode), to.id, bridge.toNode) ||
                !Contains(to.Connections(bridge.toNode), from.id, bridge.fromNode))
                throw new FormatException("Straight connection is missing from nodeS/nodeE: " + key);
            if (!SamePoint(from.Endpoint(bridge.fromNode), bridge.points[0]) ||
                !SamePoint(to.Endpoint(bridge.toNode), bridge.points[bridge.points.Length - 1]))
                throw new FormatException("Straight connection points do not meet their road endpoints: " + key);
        }
        foreach (var road in roads)
        {
            for (int node = 0; node < 2; node++)
            {
                var connections = road.Connections(node);
                var unique = new HashSet<string>();
                int defaultRoad = node == 0 ? road.defaultRoadS : road.defaultRoadE;
                bool defaultFound = defaultRoad == 0 && connections.Length == 0;
                foreach (var connection in connections)
                {
                    if (connection == null) throw new FormatException("Null road connection.");
                    ValidateEndpoint(lookup, connection.roadNo, connection.enterNode);
                    string key = LinkKey(road.id, node, connection.roadNo, connection.enterNode);
                    if (!unique.Add(key)) throw new FormatException("Duplicate connection: " + key);
                    var target = lookup[connection.roadNo];
                    if (road.id == target.id) throw new FormatException("Self connection: " + key);
                    if (!Contains(target.Connections(connection.enterNode), road.id, node))
                        throw new FormatException("Missing reciprocal connection: " + key);
                    if (!SamePoint(road.Endpoint(node), target.Endpoint(connection.enterNode)) && !bridges.Contains(key))
                        throw new FormatException("Disconnected endpoints require mainRoad points: " + key);
                    if (connection.roadNo == defaultRoad) defaultFound = true;
                }
                if (!defaultFound) throw new FormatException("Invalid default road on road " + road.id + ", node " + node);
            }
        }
    }

    private static string LinkKey(int a, int an, int b, int bn)
    {
        return a < b ? $"{a}:{an}-{b}:{bn}" : $"{b}:{bn}-{a}:{an}";
    }

    private static void ValidateEndpoint(Dictionary<int, SplineJsonRoad> roads, int id, int node)
    {
        if (!roads.ContainsKey(id) || (node != 0 && node != 1))
            throw new FormatException($"Invalid endpoint {id}:{node}.");
    }

    private static bool Contains(SplineJsonConnection[] links, int id, int node)
    {
        foreach (var link in links)
            if (link != null && link.roadNo == id && link.enterNode == node) return true;
        return false;
    }

    private static bool SamePoint(float[] a, float[] b)
    {
        return Math.Abs(a[0] - b[0]) < 0.001f && Math.Abs(a[1] - b[1]) < 0.001f;
    }

    private static void ValidatePoints(float[][] points, string context)
    {
        if (points == null || points.Length < 2) throw new FormatException(context + " requires at least two points.");
        for (int i = 0; i < points.Length; i++)
        {
            ValidatePoint(points[i], context);
            if (i > 0 && SamePoint(points[i - 1], points[i]))
                throw new FormatException(context + " contains consecutive duplicate points.");
        }
    }

    private static void ValidatePoint(float[] point, string context)
    {
        if (point == null || point.Length != 2 || float.IsNaN(point[0]) || float.IsNaN(point[1]) ||
            float.IsInfinity(point[0]) || float.IsInfinity(point[1]))
            throw new FormatException(context + " requires finite [x,y] coordinates.");
    }
}

[DataContract]
public sealed class SplineJsonRoad
{
    // DataContractJsonSerializer bypasses constructors/field initializers.
    [OnDeserializing]
    private void SetDefaults(StreamingContext context)
    {
        kind = "main";
        bidirectional = true;
    }

    [DataMember] public int id;
    [DataMember] public string label;
    [DataMember] public string kind;
    [DataMember] public bool bidirectional;
    [DataMember] public SplineJsonConnection[] nodeS;
    [DataMember] public SplineJsonConnection[] nodeE;
    [DataMember] public int defaultRoadS;
    [DataMember] public int defaultRoadE;
    [DataMember] public float[][] points;
    [DataMember] public float[] controlPoint;
    public SplineJsonConnection[] Connections(int node) => node == 0 ? nodeS : nodeE;
    public float[] Endpoint(int node) => points[node == 0 ? 0 : points.Length - 1];
}

[Serializable, DataContract]
public sealed class SplineJsonConnection
{
    [DataMember] public int roadNo;
    // direction 0 enters at S and travels S->E; direction 1 enters at E and travels E->S.
    // Preserve enterNode in Unity scene serialization and the existing movement API.
    [DataMember(Name = "direction")] public int enterNode;
    [DataMember(Name = "enterNode", EmitDefaultValue = false)] private int? legacyEnterNode;

    [OnDeserializing]
    private void SetDefaults(StreamingContext context) { enterNode = -1; }

    [OnDeserialized]
    private void ReadLegacyDirection(StreamingContext context)
    {
        if (legacyEnterNode.HasValue)
        {
            if (enterNode != -1 && enterNode != legacyEnterNode.Value)
                throw new FormatException("direction and enterNode disagree on road " + roadNo);
            enterNode = legacyEnterNode.Value;
        }
        legacyEnterNode = null;
    }
}

[DataContract]
public sealed class SplineJsonStraight
{
    [OnDeserializing]
    private void SetDefaults(StreamingContext context) { bidirectional = true; }

    [DataMember] public int fromRoadNo;
    [DataMember] public int fromNode;
    [DataMember] public int toRoadNo;
    [DataMember] public int toNode;
    [DataMember] public bool bidirectional;
    [DataMember] public float[][] points;
}
