using UnityEngine;

// Inherit the exact touch gestures, camera control, lane previews and HUD used by splinemovement.
// TouchpadManager can reference this component through its existing splinemovement field.
[DisallowMultipleComponent, AddComponentMenu("Roads/Spline JSON Movement")]
public class SplineJsonMovement : splinemovement
{
    // Compatibility component for scenes already using SplineJsonMovement.
    protected override ISplineMovementRoute ResolveMovementRoute() => ResolveJsonMovementRoute();
}
