using Godot;
using GodotInputEvent = Godot.InputEvent;

namespace Mahjong.Client.Input;

/// <summary>触摸指针源：ScreenTouch/ScreenDrag 转成 PointerEvent，Index 为手指编号。</summary>
public sealed class TouchPointerSource : IPointerSource
{
    private readonly Queue<PointerEvent> _events = new();

    /// <inheritdoc />
    public void Push(GodotInputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventScreenTouch touch:
                _events.Enqueue(new PointerEvent(
                    touch.Pressed ? PointerPhase.Down : PointerPhase.Up,
                    touch.Index,
                    touch.Position));
                break;
            case InputEventScreenDrag drag:
                _events.Enqueue(new PointerEvent(PointerPhase.Move, drag.Index, drag.Position));
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
