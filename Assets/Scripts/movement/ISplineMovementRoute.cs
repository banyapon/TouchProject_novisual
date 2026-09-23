using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using CarState = RoadNetworkSplineCreator.CarState;
using RoadData = RoadNetworkSplineCreator.RoadData;
using HistoryEntry = RoadNetworkSplineCreator.RouteHistoryEntry;
using MoveMode = RoadNetworkSplineCreator.MoveMode;

/// <summary>The route data used by the shared touch, history and preview controller.</summary>
public interface ISplineMovementRoute
{
    SplineContainer Container { get; }
    RoadData GetRoadData(int roadNo);
    void EnsureHistory(CarState car);
    HistoryEntry GetForwardHistory(CarState car);
    void SyncLaneWithForwardHistory(CarState car);
    void MoveCarLoop(CarState car, float distance, MoveMode mode);
    Vector3 EvaluateRoadPosition(CarState car);
    Vector3 EvaluateRoadForward(CarState car);
    Vector3 EvaluateRoadPoint(int roadNo, float normalizedDistance);
    bool IsJunctionTraversalRoad(int roadNo);
    bool IsTraversingConnection(CarState car);
    string GetConnectionLabel(CarState car);
    void AppendConnectionPreview(List<Vector3> points, int fromRoad, int fromDir,
        int toRoad, int enterNode, int samples, float height);
}

// Keep the original scene/component and its movement behaviour unchanged.
public sealed class LegacySplineMovementRoute : ISplineMovementRoute
{
    public RoadNetworkSplineCreator Source { get; }
    public LegacySplineMovementRoute(RoadNetworkSplineCreator source) { Source = source; }
    public SplineContainer Container => Source != null ? Source.GetComponent<SplineContainer>() : null;
    public RoadData GetRoadData(int id) => Source.GetRoadData(id);
    public void EnsureHistory(CarState car) => Source.EnsureHistory(car);
    public HistoryEntry GetForwardHistory(CarState car) => Source.GetForwardHistory(car);
    public void SyncLaneWithForwardHistory(CarState car) => Source.SyncLaneWithForwardHistory(car);
    public void MoveCarLoop(CarState car, float distance, MoveMode mode) => Source.MoveCarLoop(car, distance, mode);
    public Vector3 EvaluateRoadPosition(CarState car) => Source.EvaluateRoadPosition(car);
    public Vector3 EvaluateRoadForward(CarState car) => Source.EvaluateRoadForward(car);
    public bool IsJunctionTraversalRoad(int id) => Source.IsJunctionTraversalRoad(id);
    public bool IsTraversingConnection(CarState car) => false;
    public string GetConnectionLabel(CarState car) => "";
    public Vector3 EvaluateRoadPoint(int roadNo, float normalizedDistance)
    {
        int index = roadNo - 1;
        if (Container == null || index < 0 || index >= Container.Splines.Count) return Source.transform.position;
        Vector3 localPoint = Container.Splines[index].EvaluatePosition(normalizedDistance);
        return Container.transform.TransformPoint(localPoint);
    }
    public void AppendConnectionPreview(List<Vector3> points, int fromRoad, int fromDir,
        int toRoad, int enterNode, int samples, float height) { }
}
