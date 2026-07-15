namespace RoutesToGlory.Game
{
    /// <summary>
    /// Fiction: survey fleets pre-scan alien worlds for habitability and resources
    /// before human scouts arrive. When enabled, the Unity client skips fog-of-war
    /// rendering and exploration gating so GPU/CPU budget goes to terrain tiles.
    /// </summary>
    public static class RtgWorldScanSettings
    {
        /// <summary>When true, the full world map is visible from mission start.</summary>
        public static bool PreSurveyedWorld { get; private set; } = true;

        public static void Apply(bool preSurveyedWorld) => PreSurveyedWorld = preSurveyedWorld;
    }
}
