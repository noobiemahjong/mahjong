using Godot;

namespace Mahjong.Client.Input;

/// <summary>
/// 点击路由：每帧由 Render 重新登记可见控件，实测命中用控件全局矩形。
/// Down 与 Up 命中同一控件才触发一次回调；后登记（更上层）的控件优先命中。
/// </summary>
public sealed class ClickRouter
{
    private readonly List<Control> _targets = [];
    private readonly List<Action> _callbacks = [];
    private readonly Dictionary<int, Control?> _downTargets = [];

    /// <summary>登记一个可点击控件与回调；同一帧内重复登记以最后一次为准（命中后取最早登记项）。</summary>
    /// <param name="target">命中判定用的控件。</param>
    /// <param name="onClick">Down/Up 命中同一控件时调用一次。</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 或 <paramref name="onClick"/> 为 null。</exception>
    public void Register(Control target, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(onClick);
        _targets.Add(target);
        _callbacks.Add(onClick);
    }

    /// <summary>开始新一帧：清空上一帧的登记；Down 记录跨帧保留。</summary>
    public void BeginFrame()
    {
        _targets.Clear();
        _callbacks.Clear();
    }

    /// <summary>喂入一个指针事件：Down 记起点控件；Up 与起点同一控件时触发一次。</summary>
    /// <param name="pointerEvent">指针事件。</param>
    public void Feed(PointerEvent pointerEvent)
    {
        switch (pointerEvent.Phase)
        {
            case PointerPhase.Down:
                _downTargets[pointerEvent.Index] = HitTest(pointerEvent.Position);
                break;
            case PointerPhase.Up:
                if (!_downTargets.Remove(pointerEvent.Index, out Control? downTarget) || downTarget is null)
                {
                    return;
                }

                Control? upTarget = HitTest(pointerEvent.Position);
                if (upTarget is null || !ReferenceEquals(upTarget, downTarget))
                {
                    return;
                }

                int index = _targets.IndexOf(upTarget);
                if (index >= 0)
                {
                    _callbacks[index]();
                }

                break;
            default:
                // Move 不参与点击判定；拖出再抬起由 Up 的命中结果自然排除。
                break;
        }
    }

    /// <summary>从后往前命中（后登记=更上层）；不可见或已释放的控件跳过。</summary>
    private Control? HitTest(Vector2 position)
    {
        for (int index = _targets.Count - 1; index >= 0; index--)
        {
            Control target = _targets[index];
            if (!GodotObject.IsInstanceValid(target) || !target.IsVisibleInTree())
            {
                continue;
            }

            if (target.GetGlobalRect().HasPoint(position))
            {
                return target;
            }
        }

        return null;
    }
}
