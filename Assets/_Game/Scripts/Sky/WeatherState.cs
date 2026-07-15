namespace IslandGame.Sky
{
    /// <summary>
    /// The weather states the WeatherController cycles through. Extend by
    /// appending (values may end up in save data eventually) and adding the
    /// new state to the controller's transition table + per-state targets —
    /// consumers read the controller's smoothed signals (Precipitation01,
    /// WindStrength01, StormIntensity01), so most never branch on the enum.
    /// </summary>
    public enum WeatherState
    {
        Clear = 0,
        Rain = 1,
        Storm = 2,
    }
}
