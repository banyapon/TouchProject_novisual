using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using CarState = RoadNetworkSplineCreator.CarState;
using RoadData = RoadNetworkSplineCreator.RoadData;
using Connection = RoadNetworkSplineCreator.RoadConnection;
using HistoryEntry = RoadNetworkSplineCreator.RouteHistoryEntry;
using MoveMode = RoadNetworkSplineCreator.MoveMode;

/// <summary>Distance-based traversal of SplineJson roads, including unnumbered straight spans.</summary>
public sealed class SplineJsonMovementRoute : ISplineMovementRoute
{
    private sealed class Path
    {
        public readonly SplineContainer container;
        public readonly int index;
        private readonly float[] distances = new float[257];
        public float Length => distances[distances.Length - 1];

        public Path(SplineContainer owner, int splineIndex)
        {
            container = owner; index = splineIndex;
            Vector3 previous = container.EvaluatePosition(index, 0f);
            for (int i = 1; i < distances.Length; i++)
            {
                Vector3 point = container.EvaluatePosition(index, i / 256f);
                distances[i] = distances[i - 1] + Vector3.Distance(previous, point);
                previous = point;
            }
            if (float.IsNaN(Length) || float.IsInfinity(Length) || Length < 0.00001f)
                throw new InvalidOperationException("A JSON movement spline has no usable world-space length.");
        }

        private float Parameter(float distance)
        {
            distance = Mathf.Clamp(distance, 0f, Length);
            int upper = Array.BinarySearch(distances, distance);
            if (upper >= 0) return upper / 256f;
            upper = ~upper;
            if (upper <= 0) return 0f;
            if (upper >= distances.Length) return 1f;
            return (upper - 1 + Mathf.InverseLerp(distances[upper - 1], distances[upper], distance)) / 256f;
        }

        public Vector3 Position(float distance) => container.EvaluatePosition(index, Parameter(distance));
        public Vector3 Forward(float distance)
        {
            float t = Parameter(distance);
            Vector3 tangent = container.EvaluateTangent(index, t);
            if (tangent.sqrMagnitude < 0.000001f)
            {
                var spline = container.Splines[index];
                Vector3 delta = (Vector3)spline.EvaluatePosition(Mathf.Min(1f, t + 0.01f)) -
                    (Vector3)spline.EvaluatePosition(Mathf.Max(0f, t - 0.01f));
                tangent = container.transform.TransformVector(delta);
            }
            float magnitude = tangent.magnitude;
            return magnitude > 0.0000001f ? tangent / magnitude : container.transform.forward;
        }
    }

    private sealed class Route
    {
        public SplineJson.RoadBinding binding;
        public RoadData data;
        public Path path;
    }

    private sealed class Bridge
    {
        public Path path;
        public bool reversed;
        public float Length => path.Length;
        public Vector3 Position(float distance) => path.Position(reversed ? Length - distance : distance);
        public Vector3 Forward(float distance) => path.Forward(reversed ? Length - distance : distance) * (reversed ? -1f : 1f);
    }

    private sealed class Crossing
    {
        public Bridge bridge;
        public int fromRoad, fromNode;
        public Connection target;
        public bool backtracking;
        public float distance;
    }

    public SplineJson Source { get; }
    public SplineContainer Container => Source != null ? Source.Container : null;
    private readonly Dictionary<int, Route> routes = new Dictionary<int, Route>();
    private readonly Dictionary<string, Bridge> bridges = new Dictionary<string, Bridge>();
    private readonly Dictionary<CarState, Crossing> crossings = new Dictionary<CarState, Crossing>();
    private IReadOnlyList<SplineJson.RoadBinding> cachedBindings;
    private Matrix4x4 cachedTransform, cachedStraightTransform;

    public SplineJsonMovementRoute(SplineJson source) { Source = source; }

    public bool EnsureReady()
    {
        if (Source == null) return false;
        if (Source.Roads.Count == 0 && !Source.Refresh()) return false;
        if (Container.Splines.Count != Source.Roads.Count)
            throw new InvalidOperationException("SplineJson road count changed. Refresh the JSON splines first.");
        Matrix4x4 straightMatrix = Source.StraightContainer != null ? Source.StraightContainer.transform.localToWorldMatrix : Matrix4x4.identity;
        if (ReferenceEquals(cachedBindings, Source.Roads) && cachedTransform == Source.transform.localToWorldMatrix &&
            cachedStraightTransform == straightMatrix) return true;
        routes.Clear(); bridges.Clear(); crossings.Clear();
        foreach (var binding in Source.Roads)
        {
            var path = new Path(Container, binding.splineIndex);
            routes.Add(binding.id, new Route
            {
                binding = binding, path = path,
                data = new RoadData
                {
                    id = binding.id, length = path.Length,
                    nodeS = Convert(binding.nodeS), nodeE = Convert(binding.nodeE),
                    defaultRoadS = binding.defaultRoadS, defaultRoadE = binding.defaultRoadE
                }
            });
        }
        foreach (var route in routes.Values)
        {
            route.data.laneS = SortLanes(route, 1, route.data.nodeS);
            route.data.laneE = SortLanes(route, 0, route.data.nodeE);
            route.data.defaultLaneS = DefaultLane(route, 1, route.data.laneS, route.data.defaultRoadS);
            route.data.defaultLaneE = DefaultLane(route, 0, route.data.laneE, route.data.defaultRoadE);
        }
        if (Source.Straights.Count > 0 && (Source.StraightContainer == null ||
            Source.StraightContainer.Splines.Count != Source.Straights.Count))
            throw new InvalidOperationException("SplineJson straight connections are missing. Click Refresh first.");
        for (int i = 0; i < Source.Straights.Count; i++)
        {
            var straight = Source.Straights[i];
            var path = new Path(Source.StraightContainer, i);
            bridges.Add(Key(straight.fromRoadNo, straight.fromNode, straight.toRoadNo, straight.toNode), new Bridge { path = path });
            bridges.Add(Key(straight.toRoadNo, straight.toNode, straight.fromRoadNo, straight.fromNode), new Bridge { path = path, reversed = true });
        }
        cachedBindings = Source.Roads;
        cachedTransform = Source.transform.localToWorldMatrix;
        cachedStraightTransform = straightMatrix;
        return true;
    }

    private static string Key(int road, int node, int nextRoad, int nextNode) => $"{road}:{node}>{nextRoad}:{nextNode}";
    private static Connection[] Convert(SplineJsonConnection[] connections)
    {
        var result = new Connection[connections.Length];
        for (int i = 0; i < result.Length; i++) result[i] = new Connection(connections[i].roadNo, connections[i].enterNode);
        return result;
    }

    private Connection[] SortLanes(Route route, int dir, Connection[] connections)
    {
        var legal = new List<Connection>();
        foreach (var connection in connections)
            if (routes.TryGetValue(connection.roadNo, out var next) && (connection.enterNode == 0 || next.binding.bidirectional))
                legal.Add(connection);
        Vector3 forward = route.path.Forward(route.path.Length * .5f) * (dir == 0 ? 1f : -1f);
        legal.Sort((a, b) => TurnAngle(forward, a).CompareTo(TurnAngle(forward, b)));
        return legal.ToArray();
    }

    private float TurnAngle(Vector3 forward, Connection next)
    {
        var path = routes[next.roadNo].path;
        Vector3 nextForward = path.Forward(path.Length * .5f) * (next.enterNode == 0 ? 1f : -1f);
        forward.y = 0; nextForward.y = 0;
        return Vector3.SignedAngle(forward, nextForward, Vector3.up);
    }

    private int DefaultLane(Route route, int dir, Connection[] lanes, int preferred)
    {
        int best = 0;
        float bestAngle = float.PositiveInfinity;
        Vector3 forward = route.path.Forward(route.path.Length * .5f) * (dir == 0 ? 1f : -1f);
        for (int i = 0; i < lanes.Length; i++)
        {
            if (lanes[i].roadNo == preferred) return i;
            float angle = Mathf.Abs(TurnAngle(forward, lanes[i]));
            if (angle < bestAngle) { best = i; bestAngle = angle; }
        }
        return best;
    }

    public RoadData GetRoadData(int id) => routes.TryGetValue(id, out var route) ? route.data : null;
    public string GetRoadLabel(CarState car)
    {
        if (car == null) return "-";
        string id = car.roadNo.ToString();
        if (!IsJunctionTraversalRoad(car.roadNo)) return id;
        var road = routes[car.roadNo].data;
        // Derive names from the actual endpoints, not a fixed label in JSON.
        int start = EndpointRoad(road.nodeS, road.defaultRoadS);
        int end = EndpointRoad(road.nodeE, road.defaultRoadE);
        if (start <= 0 || end <= 0) return id;
        return car.dir == 0 ? $"{id}({start}-{end})" : $"{id}({end}-{start})";
    }

    private static int EndpointRoad(Connection[] connections, int preferred)
    {
        foreach (var connection in connections)
            if (connection.roadNo == preferred) return preferred;
        return connections.Length == 1 ? connections[0].roadNo : 0;
    }

    public bool IsJunctionTraversalRoad(int id) => routes.TryGetValue(id, out var route) && route.binding.kind == "turn";
    public bool IsTraversingConnection(CarState car) => car != null && crossings.ContainsKey(car);
    public string GetConnectionLabel(CarState car)
    {
        return crossings.TryGetValue(car, out var crossing)
            ? $"{crossing.fromRoad}:{crossing.fromNode} → {crossing.target.roadNo}:{crossing.target.enterNode} ({crossing.distance:0.0}/{crossing.bridge.Length:0.0})" : "";
    }
    public Vector3 EvaluateRoadPoint(int id, float normalizedDistance)
    {
        return routes.TryGetValue(id, out var route) ? route.path.Position(Mathf.Clamp01(normalizedDistance) * route.path.Length) : Source.transform.position;
    }
    public Vector3 EvaluateRoadPosition(CarState car)
    {
        if (crossings.TryGetValue(car, out var crossing)) return crossing.bridge.Position(crossing.distance);
        return routes.TryGetValue(car.roadNo, out var route) ? route.path.Position(car.currentPos) : Source.transform.position;
    }
    public Vector3 EvaluateRoadForward(CarState car)
    {
        if (crossings.TryGetValue(car, out var crossing))
            return crossing.bridge.Forward(crossing.distance) * (crossing.backtracking ? -1f : 1f);
        return routes.TryGetValue(car.roadNo, out var route)
            ? route.path.Forward(car.currentPos) * (car.dir == 0 ? 1f : -1f) : Source.transform.forward;
    }

    public void AppendConnectionPreview(List<Vector3> points, int fromRoad, int fromDir,
        int toRoad, int enterNode, int samples, float height)
    {
        if (!bridges.TryGetValue(Key(fromRoad, fromDir == 0 ? 1 : 0, toRoad, enterNode), out var bridge)) return;
        int steps = Mathf.Max(2, samples);
        for (int i = 0; i < steps; i++)
            points.Add(bridge.Position(bridge.Length * i / (steps - 1f)) + Vector3.up * height);
    }

    public void EnsureHistory(CarState car)
    {
        if (!routes.ContainsKey(car.roadNo))
        {
            car.roadNo = Source.Roads[0].id;
            car.historyIndex = -1;
            ClearPending(car);
        }
        car.dir = Mathf.Clamp(car.dir, 0, 1);
        car.currentPos = Mathf.Clamp(car.currentPos, 0f, routes[car.roadNo].data.length);
        if (car.history == null) car.history = new List<HistoryEntry>();
        if (car.history.Count == 0 || car.historyIndex < 0 || car.historyIndex >= car.history.Count)
        {
            car.history.Clear();
            car.history.Add(new HistoryEntry(car.roadNo, car.dir, car.dir));
            car.historyIndex = 0;
        }
    }

    public HistoryEntry GetForwardHistory(CarState car)
    {
        return car.history != null && car.historyIndex >= 0 && car.historyIndex + 1 < car.history.Count
            ? car.history[car.historyIndex + 1] : null;
    }

    private static void ClearPending(CarState car)
    {
        car.pendingNextRoad = -1; car.pendingEnterNode = -1;
        car.hasPendingSelection = false; car.routeChoiceChanged = false;
    }

    public void SyncLaneWithForwardHistory(CarState car)
    {
        var road = GetRoadData(car.roadNo);
        if (road == null) return;
        var lanes = car.dir == 0 ? road.laneE : road.laneS;
        if (lanes.Length == 0) { car.currentLane = 0; return; }
        var forward = GetForwardHistory(car);
        if (forward != null)
            for (int i = 0; i < lanes.Length; i++)
                if (lanes[i].roadNo == forward.roadNo && lanes[i].enterNode == forward.enterNode)
                { car.currentLane = i; return; }
        car.currentLane = Mathf.Clamp(car.dir == 0 ? road.defaultLaneE : road.defaultLaneS, 0, lanes.Length - 1);
    }

    private static bool Contains(Connection[] lanes, Connection candidate)
    {
        foreach (var lane in lanes) if (lane.roadNo == candidate.roadNo && lane.enterNode == candidate.enterNode) return true;
        return false;
    }

    private Connection NextConnection(CarState car, RoadData road, int exitNode)
    {
        var lanes = exitNode == 0 ? road.laneS : road.laneE;
        if (lanes.Length == 0) return default;
        if (IsJunctionTraversalRoad(road.id))
            return lanes[Mathf.Clamp(exitNode == 0 ? road.defaultLaneS : road.defaultLaneE, 0, lanes.Length - 1)];
        if (car.hasPendingSelection)
        {
            var pending = new Connection(car.pendingNextRoad, car.pendingEnterNode);
            if (Contains(lanes, pending)) return pending;
            ClearPending(car);
        }
        var forward = GetForwardHistory(car);
        if (forward != null)
        {
            var stored = new Connection(forward.roadNo, forward.enterNode);
            if (Contains(lanes, stored)) return stored;
            car.history.RemoveRange(car.historyIndex + 1, car.history.Count - car.historyIndex - 1);
        }
        return lanes[Mathf.Clamp(car.currentLane, 0, lanes.Length - 1)];
    }

    private void EnterForward(CarState car, Connection next)
    {
        var forward = GetForwardHistory(car);
        if (forward != null && forward.roadNo == next.roadNo && forward.enterNode == next.enterNode)
            car.historyIndex++;
        else
        {
            if (car.historyIndex + 1 < car.history.Count)
                car.history.RemoveRange(car.historyIndex + 1, car.history.Count - car.historyIndex - 1);
            car.history.Add(new HistoryEntry(next.roadNo, next.enterNode, next.enterNode));
            car.historyIndex = car.history.Count - 1;
        }
        car.roadNo = next.roadNo; car.dir = next.enterNode;
        car.currentPos = next.enterNode == 0 ? 0f : GetRoadData(next.roadNo).length;
        ClearPending(car); SyncLaneWithForwardHistory(car);
    }

    private void EnterPrevious(CarState car)
    {
        car.historyIndex--;
        var previous = car.history[car.historyIndex];
        car.roadNo = previous.roadNo; car.dir = previous.dirOnEnter;
        car.currentPos = car.dir == 0 ? GetRoadData(car.roadNo).length : 0f;
        ClearPending(car); SyncLaneWithForwardHistory(car);
    }

    public void MoveCarLoop(CarState car, float distance, MoveMode mode)
    {
        if (!EnsureReady() || car == null || float.IsNaN(distance) || float.IsInfinity(distance)) return;
        EnsureHistory(car);
        float remain = Mathf.Max(0f, distance);
        int safety = 0;
        while (remain > 0.000001f && safety++ < 128)
        {
            if (crossings.TryGetValue(car, out var crossing))
            {
                bool advance = (mode == MoveMode.Backward) == crossing.backtracking;
                float available = advance ? crossing.bridge.Length - crossing.distance : crossing.distance;
                float consumed = Mathf.Min(remain, available);
                crossing.distance += advance ? consumed : -consumed;
                remain -= consumed;
                if (consumed < available) break;
                crossings.Remove(car);
                if (advance)
                {
                    if (crossing.backtracking) EnterPrevious(car);
                    else EnterForward(car, crossing.target);
                }
                continue;
            }
            var road = GetRoadData(car.roadNo);
            bool towardE = (car.dir == 0) == (mode == MoveMode.Forward);
            float availableRoad = towardE ? road.length - car.currentPos : car.currentPos;
            if (remain <= availableRoad)
            {
                car.currentPos += towardE ? remain : -remain;
                break;
            }
            remain -= availableRoad;
            car.currentPos = towardE ? road.length : 0f;
            int exitNode = towardE ? 1 : 0;
            Connection next;
            if (mode == MoveMode.Backward)
            {
                if (car.historyIndex <= 0) break;
                var previous = car.history[car.historyIndex - 1];
                next = new Connection(previous.roadNo, previous.dirOnEnter == 0 ? 1 : 0);
                if (!Contains(exitNode == 0 ? road.nodeS : road.nodeE, next) || GetRoadData(next.roadNo) == null) break;
            }
            else next = NextConnection(car, road, exitNode);
            if (!next.IsValid || GetRoadData(next.roadNo) == null) break;
            if (bridges.TryGetValue(Key(car.roadNo, exitNode, next.roadNo, next.enterNode), out var bridge))
            {
                crossings.Add(car, new Crossing
                {
                    bridge = bridge, fromRoad = car.roadNo, fromNode = exitNode, target = next,
                    backtracking = mode == MoveMode.Backward
                });
                continue;
            }
            Vector3 destination = routes[next.roadNo].path.Position(next.enterNode == 0 ? 0f : routes[next.roadNo].path.Length);
            if (Vector3.Distance(EvaluateRoadPosition(car), destination) > .01f)
            {
                Debug.LogError($"SplineJsonMovement: missing connection geometry {car.roadNo}:{exitNode} -> {next}.");
                break;
            }
            if (mode == MoveMode.Backward) EnterPrevious(car); else EnterForward(car, next);
        }
        if (safety >= 128) Debug.LogWarning("SplineJsonMovement stopped at the route transition safety limit.");
    }
}
