using Godot;
using GodotInputEvent = Godot.InputEvent;

namespace Mahjong.Client.Input;

/// <summary>鼠标指针源：只把鼠标按键与移动转成 PointerEvent，Index 为按键编号。
/// 触摸仿真出的鼠标事件（Device 为 -1）被丢弃，避免同一触摸经两条源重复触发。</summary>
public sealed class MousePointerSource : IPointerSource
{
    /// <summary>引擎为触摸仿真鼠标事件使用的设备号（DEVICE_ID_EMULATION）。</summary>
    private const int EmulatedDeviceId = -1;

    private readonly Queue<PointerEvent> _events = new();

    /// <inheritdoc />
    public void Push(GodotInputEvent inputEvent)
    {
        if (inputEvent.Device == EmulatedDeviceId)
        {
            return;
        }

        switch (inputEvent)
        {
            case InputEventMouseButton button:
                _events.Enqueue(new PointerEvent(
                    button.Pressed ? PointerPhase.Down : PointerPhase.Up,
                    (int)button.ButtonIndex,
                    button.Position));
                break;
            case InputEventMouseMotion motion:
                _events.Enqueue(new PointerEvent(PointerPhase.Move, 0, motion.Position));
                break;
        }
    }

    /// <inheritdoc />
    public bool TryRead(out PointerEvent pointerEvent)
    {
        if (_events.Count == 0)
        {
            pointerEvent = default;
            return false;
        }

        pointerEvent = _events.Dequeue();
        return true;
    }
}
