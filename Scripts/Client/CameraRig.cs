using Godot;

namespace Fantasia.Client;

/// RuneScape-style orbit camera: arrow keys / middle-drag rotate, wheel zooms.
public partial class CameraRig : Node3D
{
    public Camera3D Cam { get; private set; }
    public Node3D Follow;
    public float Yaw = 0f;
    public float Pitch = Mathf.DegToRad(36f);
    public float Distance = 12f;
    float targetDistance = 12f;
    bool dragging;
    Vector3 focus;

    public override void _Ready()
    {
        Cam = new Camera3D { Fov = 55, Near = 0.1f, Far = 400f, Current = true };
        AddChild(Cam);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle) dragging = mb.Pressed;
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) targetDistance = Mathf.Max(4f, targetDistance - 1.2f * Settings.ZoomSpeed);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) targetDistance = Mathf.Min(32f, targetDistance + 1.2f * Settings.ZoomSpeed);
        }
        else if (e is InputEventMouseMotion mm && dragging)
        {
            float inv = Settings.InvertCamera ? -1f : 1f;
            Yaw -= mm.Relative.X * 0.006f * Settings.CameraSpeed * inv;
            Pitch = Mathf.Clamp(Pitch + mm.Relative.Y * 0.004f * Settings.CameraSpeed * inv, Mathf.DegToRad(15), Mathf.DegToRad(85));
        }
    }

    public void SnapToFollow()
    {
        if (Follow != null) focus = Follow.GlobalPosition;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool typing = GetViewport().GuiGetFocusOwner() is LineEdit;
        if (!typing)
        {
            float k = Settings.CameraSpeed;
            if (Input.IsKeyPressed(Key.Left)) Yaw += dt * 2.0f * k;
            if (Input.IsKeyPressed(Key.Right)) Yaw -= dt * 2.0f * k;
            if (Input.IsKeyPressed(Key.Up)) Pitch = Mathf.Min(Mathf.DegToRad(85), Pitch + dt * 1.2f * k);
            if (Input.IsKeyPressed(Key.Down)) Pitch = Mathf.Max(Mathf.DegToRad(15), Pitch - dt * 1.2f * k);
        }
        Distance = Mathf.Lerp(Distance, targetDistance, Mathf.Min(1f, dt * 8f));
        if (Follow != null) focus = focus.Lerp(Follow.GlobalPosition, Mathf.Min(1f, dt * 10f));
        var target = focus + new Vector3(0, 1.2f, 0);
        var offset = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(Pitch), Mathf.Sin(Pitch), Mathf.Cos(Yaw) * Mathf.Cos(Pitch)) * Distance;
        Cam.GlobalPosition = target + offset;
        Cam.LookAt(target, Vector3.Up);
    }

    public void ResetNorth() { Yaw = 0f; }

    /// Test harnesses: jump straight to a zoom level.
    public void SetZoom(float d) { targetDistance = Distance = d; }
}
