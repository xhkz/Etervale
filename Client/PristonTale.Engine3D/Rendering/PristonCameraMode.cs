using System.Text.Json.Serialization;

namespace PristonTale.Engine3D.Rendering;

// The original's three camera modes, in the order its bar button and Z key cycle them: Manual moves only by the player's
// own orbit and zoom, Auto also turns behind the moving character, Lock holds a fixed distance and pitch.
[JsonConverter(typeof(JsonStringEnumConverter<PristonCameraMode>))]
public enum PristonCameraMode
{
    Manual,
    Auto,
    Lock
}
