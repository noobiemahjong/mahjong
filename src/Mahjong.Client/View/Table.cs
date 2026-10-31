using System;
using System.Collections.Generic;
using Godot;
using Mahjong.Client.Input;
using Mahjong.Core.Flow;

namespace Mahjong.Client.View;

/// <summary>
/// 牌桌视图：把 <see cref="TableViewState"/> 落到固定节点树，并向 <see cref="ClickRouter"/> 登记可点击控件。
/// 只做文本/可见性/布局与交互登记，不引用会话、命令或输入路由；点击只回调 (HandTileView, CommandVerb)。
/// </summary>
public partial class Table : Control
{
    private const int TileWidth = 46;
    private const int TileHeight = 62;
    private const int MeldFontSize = 14;
    private const int DiscardFontSize = 13;
    private const int ActionButtonWidth = 104;

    private static readonly Color TileBackground = new(0.16f, 0.2f, 0.26f);
    private static readonly Color TileBorderColor = new(0.45f, 0.5f, 0.58f);
    private static readonly Color SelectedBorderColor = new(1f, 0.85f, 0.2f);
    private static readonly Color JokerBorderColor = new(1f, 0f, 0f);

    private readonly PlayerWidgets[] _players = new PlayerWidgets[4];
    private readonly List<(Panel Panel, HandTileView View)> _handWidgets = [];
    private readonly Dictionary<CommandVerb, Button> _actionButtons = [];

    private Label _info = null!;
    private HBoxContainer _hand = null!;
    private TableViewState? _lastState;

    /// <summary>单座位的固定子控件；路径在场景里固定，只在 _Ready 解析一次。</summary>
    private sealed class PlayerWidgets
    {
        public required Label Name { get; init; }

        public required Label Score { get; init; }

        public required HBoxContainer Melds { get; init; }

        public required Control BankerMark { get; init; }

        public required Label BankerText { get; init; }

        public required Label Discards { get; init; }
    }

    public override void _Ready()
    {
        _info = GetNode<Label>("Info");
        for (int relativeSeat = 0; relativeSeat < 4; relativeSeat++)
        {
            string path = $"Players/Player{relativeSeat}";
            _players[relativeSeat] = new PlayerWidgets
            {
                Name = GetNode<Label>(path + "/Name"),
                Score = GetNode<Label>(path + "/Score"),
                Melds = GetNode<HBoxContainer>(path + "/Melds"),
                BankerMark = GetNode<Control>(path + "/BankerMark"),
                BankerText = GetNode<Label>(path + "/BankerMark/Text"),
                Discards = GetNode<Label>(path + "/Discards/List"),
            };
            _players[relativeSeat].BankerText.Text = "B";
        }

        _hand = GetNode<HBoxContainer>("Hand");
        _actionButtons[CommandVerb.Discard] = GetNode<Button>("Actions/Discard");
        _actionButtons[CommandVerb.Pong] = GetNode<Button>("Actions/Pong");
        _actionButtons[CommandVerb.Kong] = GetNode<Button>("Actions/Kong");
        _actionButtons[CommandVerb.Win] = GetNode<Button>("Actions/Win");
        _actionButtons[CommandVerb.Pass] = GetNode<Button>("Actions/Pass");
        foreach (Button button in _actionButtons.Values)
        {
            button.MouseFilter = MouseFilterEnum.Ignore;
        }
    }

    /// <summary>
    /// 渲染一帧并重新登记交互：内容不变时只重登记，避免每帧重建节点。
    /// 手牌 Panel 与可用的动作按钮登记到 <paramref name="router"/>；禁用的按钮不登记。
    /// </summary>
    /// <param name="state">本帧投影快照。</param>
    /// <param name="router">每帧由宿主 BeginFrame 清空过的点击路由。</param>
    /// <param name="onTile">手牌点击回调；参数携带真实手牌槽位。</param>
    /// <param name="onAction">动作按钮回调；只对启用中的动词登记。</param>
    public void Render(TableViewState state, ClickRouter router, Action<HandTileView> onTile, Action<CommandVerb> onAction)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(onTile);
        ArgumentNullException.ThrowIfNull(onAction);

        if (!SameContent(_lastState, state))
        {
            Apply(state);
            _lastState = state;
        }

        foreach ((Panel panel, HandTileView view) in _handWidgets)
        {
            HandTileView captured = view;
            router.Register(panel, () => onTile(captured));
        }

        foreach ((CommandVerb verb, Button button) in _actionButtons)
        {
            if (button.Disabled)
            {
                continue;
            }

            CommandVerb captured = verb;
            router.Register(button, () => onAction(captured));
        }
    }

    private void Apply(TableViewState state)
    {
        PlayerView self = state.Players[0];
        _info.Text = $"Round {state.RoundNo}/8  Wall {state.WallCount}  You {self.Score}\n{state.StatusLine}";

        for (int relativeSeat = 0; relativeSeat < 4; relativeSeat++)
        {
            PlayerView player = state.Players[relativeSeat];
            PlayerWidgets widgets = _players[relativeSeat];
            widgets.Name.Text = (player.IsCurrent ? "> " : "`  ") + player.Name;
            widgets.Score.Text = FormatSigned(player.Score);
            widgets.BankerMark.Visible = player.IsBanker;
            ApplyMelds(widgets.Melds, player.Melds);
            widgets.Discards.Text = JoinLabels(player.Discards);
        }

        ApplyHand(state);
        ApplyActions(state.EnabledActions, state.SelectedTileId is not null);
    }

    private void ApplyHand(TableViewState state)
    {
        foreach (Node child in _hand.GetChildren())
        {
            _hand.RemoveChild(child);
            child.QueueFree();
        }

        _handWidgets.Clear();
        foreach (HandTileView tile in state.Hand)
        {
            Panel panel = CreateTilePanel(tile, state.SelectedTileId == tile.Id);
            _hand.AddChild(panel);
            _handWidgets.Add((panel, tile));
        }
    }

    private static Panel CreateTilePanel(HandTileView tile, bool selected)
    {
        var panel = new Panel
        {
            CustomMinimumSize = new Vector2(TileWidth, TileHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat { BgColor = TileBackground };
        int borderWidth = tile.IsJoker || selected ? 2 : 1;
        style.BorderWidthLeft = borderWidth;
        style.BorderWidthTop = borderWidth;
        style.BorderWidthRight = borderWidth;
        style.BorderWidthBottom = borderWidth;
        style.BorderColor = tile.IsJoker ? JokerBorderColor : selected ? SelectedBorderColor : TileBorderColor;
        panel.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = tile.Label,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return panel;
    }

    private void ApplyActions(IReadOnlyList<CommandVerb> enabledActions, bool hasSelection)
    {
        foreach ((CommandVerb verb, Button button) in _actionButtons)
        {
            bool enabled = Contains(enabledActions, verb);
            if (verb == CommandVerb.Discard)
            {
                enabled &= hasSelection;
            }

            button.Disabled = !enabled;
        }
    }

    private static void ApplyMelds(HBoxContainer container, IReadOnlyList<TileView> melds)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }

        foreach (TileView meld in melds)
        {
            var label = new Label
            {
                Text = meld.Label,
                MouseFilter = MouseFilterEnum.Ignore,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.AddThemeFontSizeOverride("font_size", MeldFontSize);
            container.AddChild(label);
        }
    }

    private static string JoinLabels(IReadOnlyList<TileView> tiles)
    {
        if (tiles.Count == 0)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder(tiles[0].Label);
        for (int index = 1; index < tiles.Count; index++)
        {
            text.Append(' ').Append(tiles[index].Label);
        }

        return text.ToString();
    }

    private static bool Contains(IReadOnlyList<CommandVerb> verbs, CommandVerb verb)
    {
        for (int index = 0; index < verbs.Count; index++)
        {
            if (verbs[index] == verb)
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatSigned(int value) => value >= 0 ? $"+{value}" : value.ToString();

    /// <summary>只比较渲染相关字段；StatusLine/选中变化也触发重建。</summary>
    private static bool SameContent(TableViewState? current, TableViewState next)
    {
        if (current is null)
        {
            return false;
        }

        if (current.RoundNo != next.RoundNo
            || current.ViewerSeat != next.ViewerSeat
            || current.WallCount != next.WallCount
            || current.SelectedTileId != next.SelectedTileId
            || !string.Equals(current.StatusLine, next.StatusLine, StringComparison.Ordinal)
            || current.Players.Count != next.Players.Count
            || current.Hand.Count != next.Hand.Count
            || !SameActions(current.EnabledActions, next.EnabledActions))
        {
            return false;
        }

        for (int index = 0; index < current.Players.Count; index++)
        {
            if (!SamePlayer(current.Players[index], next.Players[index]))
            {
                return false;
            }
        }

        for (int index = 0; index < current.Hand.Count; index++)
        {
            if (current.Hand[index] != next.Hand[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SamePlayer(PlayerView current, PlayerView next) =>
        current.Seat == next.Seat
        && current.RelativeSeat == next.RelativeSeat
        && current.Name == next.Name
        && current.Score == next.Score
        && current.IsBanker == next.IsBanker
        && current.IsCurrent == next.IsCurrent
        && current.HandCount == next.HandCount
        && SameTiles(current.Melds, next.Melds)
        && SameTiles(current.Discards, next.Discards);

    private static bool SameTiles(IReadOnlyList<TileView> current, IReadOnlyList<TileView> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (int index = 0; index < current.Count; index++)
        {
            if (current[index] != next[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameActions(IReadOnlyList<CommandVerb> current, IReadOnlyList<CommandVerb> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (int index = 0; index < current.Count; index++)
        {
            if (current[index] != next[index])
            {
                return false;
            }
        }

        return true;
    }
}
