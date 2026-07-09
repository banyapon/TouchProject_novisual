using UnityEngine;

public static class TouchCalibrationSettings
{
    public const float DefaultScaleResearch = 65f / 40f;
    public const float DefaultRawVerticalDistance = 912f;
    public const float DefaultTouchPadVerticalCmDistance = 8f;
    public const float DefaultRawHorizontalDistance = 1452f;
    public const float DefaultTouchPadHorizontalCmDistance = 12.5f;

    public const string ScaleResearchKey = "TouchCalibration.ScaleResearch";
    public const string RawVerticalDistanceKey = "TouchCalibration.RawVerticalDistance";
    public const string TouchPadVerticalCmDistanceKey = "TouchCalibration.TouchPadVerticalCmDistance";
    public const string RawHorizontalDistanceKey = "TouchCalibration.RawHorizontalDistance";
    public const string TouchPadHorizontalCmDistanceKey = "TouchCalibration.TouchPadHorizontalCmDistance";

    public struct Values
    {
        public float ScaleResearch;
        public float RawVerticalDistance;
        public float TouchPadVerticalCmDistance;
        public float RawHorizontalDistance;
        public float TouchPadHorizontalCmDistance;

        public float VerticalCmPerRaw
        {
            get { return TouchPadVerticalCmDistance / Mathf.Max(RawVerticalDistance, 1f); }
        }

        public float HorizontalCmPerRaw
        {
            get { return TouchPadHorizontalCmDistance / Mathf.Max(RawHorizontalDistance, 1f); }
        }
    }

    public static Values Defaults
    {
        get
        {
            return new Values
            {
                ScaleResearch = DefaultScaleResearch,
                RawVerticalDistance = DefaultRawVerticalDistance,
                TouchPadVerticalCmDistance = DefaultTouchPadVerticalCmDistance,
                RawHorizontalDistance = DefaultRawHorizontalDistance,
                TouchPadHorizontalCmDistance = DefaultTouchPadHorizontalCmDistance
            };
        }
    }

    public static bool HasCompleteCalibration()
    {
        return PlayerPrefs.HasKey(ScaleResearchKey) &&
               PlayerPrefs.HasKey(RawVerticalDistanceKey) &&
               PlayerPrefs.HasKey(TouchPadVerticalCmDistanceKey) &&
               PlayerPrefs.HasKey(RawHorizontalDistanceKey) &&
               PlayerPrefs.HasKey(TouchPadHorizontalCmDistanceKey);
    }

    public static Values Load()
    {
        Values defaults = Defaults;
        if (!HasCompleteCalibration())
        {
            return defaults;
        }

        return new Values
        {
            ScaleResearch = PlayerPrefs.GetFloat(ScaleResearchKey, defaults.ScaleResearch),
            RawVerticalDistance = Mathf.Max(PlayerPrefs.GetFloat(RawVerticalDistanceKey, defaults.RawVerticalDistance), 1f),
            TouchPadVerticalCmDistance = Mathf.Max(PlayerPrefs.GetFloat(TouchPadVerticalCmDistanceKey, defaults.TouchPadVerticalCmDistance), 0.01f),
            RawHorizontalDistance = Mathf.Max(PlayerPrefs.GetFloat(RawHorizontalDistanceKey, defaults.RawHorizontalDistance), 1f),
            TouchPadHorizontalCmDistance = Mathf.Max(PlayerPrefs.GetFloat(TouchPadHorizontalCmDistanceKey, defaults.TouchPadHorizontalCmDistance), 0.01f)
        };
    }

    public static void Save(Values values)
    {
        PlayerPrefs.SetFloat(ScaleResearchKey, Mathf.Max(values.ScaleResearch, 0.01f));
        PlayerPrefs.SetFloat(RawVerticalDistanceKey, Mathf.Max(values.RawVerticalDistance, 1f));
        PlayerPrefs.SetFloat(TouchPadVerticalCmDistanceKey, Mathf.Max(values.TouchPadVerticalCmDistance, 0.01f));
        PlayerPrefs.SetFloat(RawHorizontalDistanceKey, Mathf.Max(values.RawHorizontalDistance, 1f));
        PlayerPrefs.SetFloat(TouchPadHorizontalCmDistanceKey, Mathf.Max(values.TouchPadHorizontalCmDistance, 0.01f));
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        PlayerPrefs.DeleteKey(ScaleResearchKey);
        PlayerPrefs.DeleteKey(RawVerticalDistanceKey);
        PlayerPrefs.DeleteKey(TouchPadVerticalCmDistanceKey);
        PlayerPrefs.DeleteKey(RawHorizontalDistanceKey);
        PlayerPrefs.DeleteKey(TouchPadHorizontalCmDistanceKey);
        PlayerPrefs.Save();
    }
}
