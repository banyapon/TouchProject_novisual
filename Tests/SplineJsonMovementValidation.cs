using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using CarState = RoadNetworkSplineCreator.CarState;
using MoveMode = RoadNetworkSplineCreator.MoveMode;

public static class SplineJsonMovementValidation
{
    private static int checks;
    private static splinemovement movement;
    private static SplineJsonMovementRoute sceneRoute;
    private static double nextCheck;
    private static int phase;
    private static Vector3 initialPosition;
    private static CarState Make(SplineJsonMovementRoute route, int road, int dir, float position)
    {
        var car = new CarState { roadNo = road, dir = dir, currentPos = position };
        route.EnsureHistory(car); route.SyncLaneWithForwardHistory(car);
        return car;
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static void Close(float actual, float expected, string message, float epsilon = .005f)
        => Check(Mathf.Abs(actual - expected) <= epsilon, message + $" ({actual} vs {expected})");
    private static void Choose(CarState car, int next, int enter)
    {
        car.pendingNextRoad = next; car.pendingEnterNode = enter; car.hasPendingSelection = true;
    }

    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var source = new GameObject("JSON Test Network").AddComponent<SplineJson>();
            source.JsonAsset = Resources.Load<TextAsset>("data/junction");
            var simplified = SplineJsonDocument.Parse(source.JsonAsset.text);
            Check(source.JsonAsset.text.Contains("\"direction\"") && source.JsonAsset.text.Contains("\"mainRoad\""), "Junction uses renamed JSON keys.");
            Check(simplified.straightConnections.Length == 2, "mainRoad imports both straight spans.");
            var legacy = SplineJsonDocument.Parse(source.JsonAsset.text.Replace("\"direction\"", "\"enterNode\"").Replace("\"mainRoad\"", "\"straightConnections\""));
            Check(legacy.straightConnections.Length == simplified.straightConnections.Length, "Legacy straightConnections still imports.");
            foreach (var jsonRoad in simplified.roads)
            {
                var oldRoad = legacy.roads.Single(x => x.id == jsonRoad.id);
                Check(jsonRoad.nodeS.Select(x => (x.roadNo, x.enterNode)).SequenceEqual(oldRoad.nodeS.Select(x => (x.roadNo, x.enterNode))) &&
                    jsonRoad.nodeE.Select(x => (x.roadNo, x.enterNode)).SequenceEqual(oldRoad.nodeE.Select(x => (x.roadNo, x.enterNode))), "New direction and legacy enterNode preserve connectivity for road " + jsonRoad.id);
            }
            Check(simplified.roads[0].id == 1 && simplified.roads[0].points[0][0] == 0f && simplified.roads[0].points[0][1] == 0f, "Road 1 starts at JSON origin [0,0].");
            Check(simplified.roads.All(x => x.bidirectional), "Omitted bidirectional defaults to true.");
            Check(simplified.roads.Where(x => x.id <= 4).All(x => x.kind == "main"), "Omitted kind defaults to main.");
            Check(simplified.roads.All(x => x.points.Length == 2), "Compact roads keep only the required endpoints.");
            var oneWay = SplineJsonDocument.Parse("{\"roads\":[{\"id\":1,\"bidirectional\":false,\"points\":[[0,0],[0,1]]}]}");
            Check(!oneWay.roads[0].bidirectional, "Explicit false overrides the default.");
            Check(oneWay.roads[0].nodeS.Length == 0 && oneWay.roads[0].nodeE.Length == 0, "Omitted endpoint connections stay empty.");
            Check(source.Refresh(), "Refresh JSON source.");
            var route = new SplineJsonMovementRoute(source);
            Check(route.EnsureReady(), "Build movement cache.");
            var resolveRoute = typeof(splinemovement).GetMethod("ResolveMovementRoute", BindingFlags.Instance | BindingFlags.NonPublic);
            var controllerObject = new GameObject("Shared controller validation");
            controllerObject.SetActive(false);
            var sharedController = controllerObject.AddComponent<splinemovement>();
            Check(resolveRoute.Invoke(sharedController, null) is SplineJsonMovementRoute, "Base controller discovers JSON when no legacy network exists.");
            var legacyObject = new GameObject("Legacy provider validation");
            legacyObject.SetActive(false);
            var legacyNetwork = legacyObject.AddComponent<RoadNetworkSplineCreator>();
            var controllerSettings = new SerializedObject(sharedController);
            controllerSettings.FindProperty("roadNetwork").objectReferenceValue = legacyNetwork;
            controllerSettings.ApplyModifiedPropertiesWithoutUndo();
            sharedController.SplineSource = source;
            Check(resolveRoute.Invoke(sharedController, null) is SplineJsonMovementRoute, "Explicit JSON source takes priority over legacy source.");
            sharedController.SplineSource = null;
            Check(resolveRoute.Invoke(sharedController, null) is LegacySplineMovementRoute, "Assigned legacy provider still works with the base controller.");
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(legacyObject);
            float arm = route.GetRoadData(1).length;
            var car = Make(route, 1, 0, arm - 1f);
            Vector3 start = route.EvaluateRoadPosition(car);
            route.MoveCarLoop(car, 3f, MoveMode.Forward);
            Check(car.roadNo == 1 && route.IsTraversingConnection(car), "Straight crossing has no invented road ID.");
            Close(Vector3.Distance(start, route.EvaluateRoadPosition(car)), 3f, "Bridge consumes real distance, without teleport.");
            Check(car.history.Count == 1, "Commit next road only on bridge arrival.");
            route.MoveCarLoop(car, 1f, MoveMode.Backward);
            Close(Vector3.Distance(start, route.EvaluateRoadPosition(car)), 2f, "Reverse partway through bridge.");
            route.MoveCarLoop(car, 2f, MoveMode.Backward);
            Check(!route.IsTraversingConnection(car) && car.history.Count == 1, "Cancel crossing without a fake history entry.");
            Close(car.currentPos, arm - 1f, "Return to exact starting position.");

            float bridgeLength = Vector3.Distance(source.Container.EvaluatePosition(0, 1), source.Container.EvaluatePosition(1, 0));
            route.MoveCarLoop(car, 1f + bridgeLength + 2f, MoveMode.Forward);
            Check(car.roadNo == 2 && car.dir == 0, "Straight traversal enters correct endpoint.");
            Close(car.currentPos, 2f, "Remainder is consumed exactly once.");
            Check(car.history.Select(x => x.roadNo).SequenceEqual(new[] {1,2}), "History retains only real road IDs.");
            route.MoveCarLoop(car, 3f, MoveMode.Backward);
            Check(car.roadNo == 2 && route.IsTraversingConnection(car), "Backtracking traverses bridge physically.");
            route.MoveCarLoop(car, 2f, MoveMode.Forward);
            Check(car.roadNo == 2 && !route.IsTraversingConnection(car), "Reverse a backward bridge crossing.");
            Close(car.currentPos, 1f, "Resume inside original road after cancelled backtrack.");
            route.MoveCarLoop(car, 1f + bridgeLength + 1f, MoveMode.Backward);
            Check(car.roadNo == 1 && car.historyIndex == 0 && car.history.Count == 2, "Reverse restores original history without deleting future.");
            Close(car.currentPos, arm - 1f, "Full reverse distance is conserved.");
            route.MoveCarLoop(car, 1f + bridgeLength + 2f, MoveMode.Forward);
            Check(car.roadNo == 2 && car.history.Count == 2, "Forward reuses stored history.");

            // Same connector, both orientations; every arm has left/straight/right.
            int[][] lanes = {new[]{1,0,5,2,6},new[]{2,1,8,1,7},new[]{3,0,7,4,5},new[]{4,1,6,3,8}};
            foreach (var row in lanes)
            {
                var data = route.GetRoadData(row[0]);
                var options = row[1] == 0 ? data.laneE : data.laneS;
                Check(options.Select(x=>x.roadNo).SequenceEqual(row.Skip(2)), "Left/right depend on current facing: road " + row[0]);
                Close(data.length, arm, "Symmetric arm lengths.");
            }
            foreach (int id in new[]{5,6,7,8})
            {
                var turn = route.GetRoadData(id);
                foreach (int enter in new[]{0,1})
                {
                    var incoming = enter == 0 ? turn.nodeS[0] : turn.nodeE[0];
                    var outgoing = enter == 0 ? turn.nodeE[0] : turn.nodeS[0];
                    var before = route.GetRoadData(incoming.roadNo);
                    int face = incoming.enterNode == 1 ? 0 : 1;
                    var c = Make(route, before.id, face, face == 0 ? before.length-1f : 1f);
                    Choose(c,id,enter);
                    route.MoveCarLoop(c,2f,MoveMode.Forward);
                    Check(c.roadNo==id && c.dir==enter, "Shared connector direction: " + id + ":" + enter);
                    Check(route.GetRoadLabel(c) == $"{id}({incoming.roadNo}-{outgoing.roadNo})", "Road label follows endpoint direction, including reversed turns.");
                    Close(c.currentPos, enter==0?1f:turn.length-1f, "Turn remainder.");
                    route.MoveCarLoop(c,turn.length,MoveMode.Forward);
                    Check(c.roadNo==outgoing.roadNo && c.dir==outgoing.enterNode, "Turn exits to specified JSON endpoint.");
                    route.MoveCarLoop(c,turn.length+2f,MoveMode.Backward);
                    Check(c.roadNo==before.id && c.dir==face && c.historyIndex==0, "Turn reverses through actual route history.");
                    Close(c.currentPos,face==0?before.length-1f:1f,"Reverse restores turn approach.");
                }
            }

            car=Make(route,1,0,arm-1f); Choose(car,5,0);
            route.MoveCarLoop(car,2f,MoveMode.Forward);
            route.MoveCarLoop(car,2f,MoveMode.Backward);
            int storedCount=car.history.Count; Choose(car,6,0);
            Check(car.history.Count==storedCount && car.history[1].roadNo==5,"Swipe does not commit history early.");
            route.MoveCarLoop(car,2f,MoveMode.Forward);
            Check(car.roadNo==6 && car.history.Count==2 && car.history[1].roadNo==6,"Crossing replaces only future branch.");
            car=Make(route,1,0,0);
            route.MoveCarLoop(car,1000,MoveMode.Backward);
            Close(car.currentPos,0,"Stop at beginning of history.");
            route.MoveCarLoop(car,1000,MoveMode.Forward);
            Check(car.roadNo==2 && !route.IsTraversingConnection(car),"Large movement handles multiple spans then stops at open end.");
            Close(car.currentPos,route.GetRoadData(2).length,"Clamp at open endpoint.");

            var preview=new System.Collections.Generic.List<Vector3>();
            route.AppendConnectionPreview(preview,1,0,2,0,24,.08f);
            Check(preview.Count==24 && Vector3.Distance(preview[0],preview[23])>18,"Preview includes actual straight span.");
            source.transform.localScale = new Vector3(2,1,3);
            Check(route.EnsureReady(),"Rebuild length cache after transform change.");
            Close(route.GetRoadData(1).length,arm*3,"World distance respects nonuniform scale.");
            Close(route.GetRoadData(3).length,arm*2,"World distance respects lateral scale.");

            EditorSceneManager.OpenScene("Assets/Demo/SplineJSON.unity");
            movement=UnityEngine.Object.FindAnyObjectByType<splinemovement>();
            Check(movement!=null && movement.SplineSource!=null,"Scene player uses splinemovement and assigned JSON source.");
            Check(UnityEngine.Object.FindObjectsByType<splinemovement>(FindObjectsSortMode.None).Length==1,"Scene has exactly one movement controller.");
            Check(movement.GetType()==typeof(splinemovement),"Scene uses the base splinemovement component directly.");
            var manager=UnityEngine.Object.FindAnyObjectByType<TouchpadManager>();
            Check(manager!=null && manager.ControlsSplineMovement(movement),"Touch manager references derived controller without duplicate input.");
            var serialized=new SerializedObject(movement);
            Check(serialized.FindProperty("startRoadNo").intValue==1 && serialized.FindProperty("startDirection").intValue==0,"Scene starts on south road facing junction.");
            Close(serialized.FindProperty("startRoadPosition").floatValue, 0f, "Avatar starts at road 1 first point.");
            Check(movement.SplineSource.TryGetTravelPose(1, 0, 0f, out var firstPoint, out _), "Scene road 1 start exists.");
            Close(Vector3.Distance(firstPoint, movement.SplineSource.transform.TransformPoint(movement.SplineSource.JsonToLocal(simplified.roads[0].points[0]))), 0f, "Rebased JSON agrees with scene spline and mesh coordinates.");
            Close(Vector3.Distance(movement.transform.position, firstPoint), 0f, "Avatar is already at road 1 start in the Editor.");
            Close(Vector3.Distance(serialized.FindProperty("startPosition").vector3Value, firstPoint), 0f, "Avatar startup position matches road 1 start.");
            Check(serialized.FindProperty("junctionHighlightSlider").objectReferenceValue!=null,"Existing slider reference retained.");
            Check(serialized.FindProperty("cameraRotateTarget").objectReferenceValue!=null,"Existing camera reference retained.");
            var rb=movement.GetComponent<Rigidbody>();
            Check(rb.isKinematic && !rb.useGravity,"Scene Rigidbody configured for spline movement.");
            // Use deterministic input packets rather than a hardware listener in this isolated test.
            manager.enabled=false;
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorApplication.playModeStateChanged+=OnPlayMode;
            EditorApplication.EnterPlaymode();
        }
        catch(Exception ex) { Fail(ex); }
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode) return;
        nextCheck=EditorApplication.timeSinceStartup+.4;
        phase=0;
        EditorApplication.update+=Tick;
    }
    private static void Input(float x,float y)
    {
        movement.EndTouchFromManager();
        movement.BeginTouchFromManager(Vector2.zero);
        movement.MoveFromTouchManager(new Vector2(x,y),new Vector2(x,y),TouchpadManager.TouchMode.Translate,1);
    }
    private static void Tick()
    {
        if(EditorApplication.timeSinceStartup<nextCheck) return;
        try
        {
            nextCheck=EditorApplication.timeSinceStartup+.25;
            var state=movement.CurrentState;
            if(phase==0)
            {
                Check(state!=null && state.roadNo==1,"Inherited Awake/Start initialize in Play Mode.");
                sceneRoute=new SplineJsonMovementRoute(movement.SplineSource); sceneRoute.EnsureReady();
                initialPosition=movement.GetComponent<Rigidbody>().position;
                Check(Vector3.Distance(initialPosition,sceneRoute.EvaluateRoadPosition(state))<.01f,"Player snaps to current spline.");
                Input(0,600);
                Close(state.currentPos,600f*(65f/40f)*(8f/912f),"Touch distance exactly matches legacy calibration.");
            }
            else if(phase==1)
            {
                Check(Vector3.Distance(movement.GetComponent<Rigidbody>().position,sceneRoute.EvaluateRoadPosition(state))<.01f,"Rigidbody follows the JSON spline.");
                float before=state.currentPos;
                Input(-10,0);
                Check(state.hasPendingSelection && state.pendingNextRoad==5,"Left swipe selects 1-3.");
                Close(state.currentPos,before,"Horizontal swipe does not move player.");
                movement.MoveFromTouchManager(new Vector2(20,0),new Vector2(30,0),TouchpadManager.TouchMode.Translate,1);
                Check(state.pendingNextRoad==5,"Only one selection per touch gesture.");
                Check(movement.GetComponent<LineRenderer>().positionCount>=2,"Selected route highlight appears.");
                Check(movement.GetComponentsInChildren<LineRenderer>().Length>=3,"Alternative highlights are retained.");
                Input(0,500);
                Check(state.roadNo==5,"Player crosses onto chosen turn spline.");
            }
            else if(phase==2)
            {
                Check(Vector3.Distance(movement.GetComponent<Rigidbody>().position,sceneRoute.EvaluateRoadPosition(state))<.01f,"Player follows curved spline.");
                Check(Vector3.Dot(movement.GetComponent<Rigidbody>().rotation*Vector3.forward,sceneRoute.EvaluateRoadForward(state))>.99f,"Player faces curve tangent.");
                Input(0,-500);
                Check(state.roadNo==1 && state.history.Count==2 && state.historyIndex==0,"Touch backtracking retains route history.");
                Input(10,0);
                Check(state.pendingNextRoad==2,"Right swipe steps from stored left lane to straight lane.");
                Input(10,0);
                Check(state.pendingNextRoad==6,"Next gesture steps to right turn.");
                Input(0,500);
                Check(state.roadNo==6 && state.history[1].roadNo==6,"Touch crossing replaces forward history.");
            }
            else if(phase==3)
            {
                var settings=new SerializedObject(movement);
                var camera=(Transform)settings.FindProperty("cameraRotateTarget").objectReferenceValue;
                Quaternion playerRotation=movement.GetComponent<Rigidbody>().rotation;
                Quaternion cameraRotation=camera.rotation;
                movement.BeginTouchFromManager(Vector2.zero);
                for(int i=1;i<=4;i++)
                    movement.MoveFromTouchManager(new Vector2(i*3,0),new Vector2(3,0),TouchpadManager.TouchMode.Rotate,2);
                Check(Quaternion.Angle(cameraRotation,camera.rotation)>.1f,"Slow two-finger rotation accumulates dead zone.");
                Check(Quaternion.Angle(playerRotation,movement.GetComponent<Rigidbody>().rotation)<.01f,"Camera rotation does not change player facing.");
                Check(state.roadNo==6,"Two-finger rotation does not move roads.");
                var slider=(Slider)settings.FindProperty("junctionHighlightSlider").objectReferenceValue;
                slider.value=.55f;
                settings.Update();
                Close(settings.FindProperty("junctionHighlightStart").floatValue,.55f,"Existing highlight slider controls inherited component.");
                EditorApplication.update-=Tick;
                File.WriteAllText("movement-validation-result.txt",$"PASS: {checks} checks. All approach directions, shared turn IDs, straight-span distance, reverse history, branch replacement, open endpoints, world scale, scene references, Play Mode Rigidbody, gesture parity, previews, camera and slider.");
                Debug.Log("SPLINE_JSON_MOVEMENT_PASS "+checks);
                EditorApplication.Exit(0);
            }
            phase++;
        }
        catch(Exception ex) { Fail(ex); }
    }
    private static void Fail(Exception ex)
    {
        EditorApplication.update-=Tick;
        File.WriteAllText("movement-validation-result.txt","FAIL: "+ex);
        Debug.LogException(ex); EditorApplication.Exit(1);
    }
}
