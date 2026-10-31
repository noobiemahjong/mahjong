using Godot;
using GodotInputEvent = Godot.InputEvent;

namespace Mahjong.Client.Input;

/// <summary>指针事件阶段；Down 与 Up 命中同一控件才构成一次点击。</summary>
public enum PointerPhase : byte
{
    /// <summary>按下。</summary>
    Down = 0,

    /// <summary>移动。</summary>
    Move = 1,

    /// <summary>抬起。</summary>
    Up = 2,
}

/// <summary>与设备无关的指针事件：阶段、指针/按键编号与视口坐标。</summary>
/// <param name="Phase">事件阶段。</param>
/// <param name="Index">鼠标为按键编号、触摸为手指编号；Down/Up 必须同编号配对。</param>
/// <param name="Position">视口坐标（与 Control.GetGlobalRect 同一坐标系）。</param>
public readonly record struct PointerEvent(PointerPhase Phase, int Index, Vector2 Position);

/// <summary>宿主输入事件到指针事件的适配器：Push 喂入引擎事件，TryRead 每帧排空。</summary>
public interface IPointerSource
{
    /// <summary>喂入一个引擎输入事件；不相关的事件被忽略。</summary>
    /// <param name="inputEvent">宿主在 _UnhandledInput 收到的原始事件。</param>
    void Push(GodotInputEvent inputEvent);

    /// <summary>排空一个已缓存的指针事件。</summary>
    /// <param name="pointerEvent">取出的事件；无事件时为 default。</param>
    /// <returns>取出一个事件返回 true；队列为空返回 false。</returns>
    bool TryRead(out PointerEvent pointerEvent);
}
