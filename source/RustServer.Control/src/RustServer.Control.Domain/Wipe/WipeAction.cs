namespace RustServer.Control.Domain.Wipe;

/// <summary>
/// The requested handling for one known wipe target.
/// </summary>
public enum WipeAction
{
    Keep,
    Delete,
    Transform
}
