namespace RoasSensor
{
    /// <summary>How much <c>Roas</c> writes to the Unity console. Defaults to
    /// <see cref="Error"/> — quiet in a release build, loud enough that a device that can
    /// never deliver anything shows up immediately instead of needing a network debugger.</summary>
    public enum RoasLogLevel
    {
        None = 0,
        Error = 1,
        Debug = 2,
    }
}
