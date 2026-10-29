// Nestation new city CC BY-NC 3.0
using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.CartridgeLoader.Cartridges;

public sealed partial class LawsUi : UIFragment
{
    private BoxContainer? _root;
    private BoxContainer _list = default!;
    private BoxContainer _editPanel = default!;
    private Label _status = default!;
    private LineEdit _input = default!;
    private Button _submit = default!;
    private Button _cancel = default!;
    private BoundUserInterface _ui = default!;
    private int _editIndex = -1;

    public override Control GetUIFragmentRoot()
    {
        return _root!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _ui = userInterface;

        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4),
        };

        _root.AddChild(new Label { Text = Loc.GetString("laws-ui-title"), HorizontalAlignment = Control.HAlignment.Center });

        _status = new Label { HorizontalAlignment = Control.HAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        _root.AddChild(_status);

        _list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_list);
        _root.AddChild(scroll);

        _editPanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 4, 0, 0),
            Visible = false,
        };

        _input = new LineEdit
        {
            PlaceHolder = Loc.GetString("laws-ui-input-placeholder"),
            HorizontalExpand = true,
            IsValid = text => text.Length <= LawsCartridgeLimits.MaxLawLength,
        };
        _input.OnTextEntered += _ => Submit();
        _editPanel.AddChild(_input);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _submit = new Button { HorizontalExpand = true };
        _submit.OnPressed += _ => Submit();
        _cancel = new Button { Text = Loc.GetString("laws-ui-cancel"), Visible = false };
        _cancel.OnPressed += _ => ResetEditor();
        buttons.AddChild(_submit);
        buttons.AddChild(_cancel);
        _editPanel.AddChild(buttons);

        _root.AddChild(_editPanel);

        ResetEditor();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not LawsUiState lawsState || _root == null)
            return;

        _editPanel.Visible = lawsState.CanWrite;
        _status.Text = Loc.GetString(lawsState.CanWrite ? "laws-ui-status-write" : "laws-ui-status-read");

        _list.RemoveAllChildren();

        if (lawsState.Laws.Count == 0)
            _list.AddChild(new Label { Text = Loc.GetString("laws-ui-empty") });

        for (var i = 0; i < lawsState.Laws.Count; i++)
        {
            _list.AddChild(BuildRow(i, lawsState.Laws[i], lawsState.CanWrite));
        }

        if (!lawsState.CanWrite || _editIndex >= lawsState.Laws.Count)
            ResetEditor();
    }

    private Control BuildRow(int index, string law, bool canWrite)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2),
        };

        row.AddChild(new Label
        {
            Text = $"{index + 1}.",
            VerticalAlignment = Control.VAlignment.Top,
            Margin = new Thickness(0, 0, 4, 0),
        });

        var text = new RichTextLabel { HorizontalExpand = true };
        text.SetMessage(FormattedMessage.FromUnformatted(law));
        row.AddChild(text);

        if (!canWrite)
            return row;

        var edit = new Button { Text = Loc.GetString("laws-ui-edit"), VerticalAlignment = Control.VAlignment.Top };
        edit.OnPressed += _ =>
        {
            _editIndex = index;
            _input.Text = law;
            _submit.Text = Loc.GetString("laws-ui-save");
            _cancel.Visible = true;
            _input.GrabKeyboardFocus();
        };
        row.AddChild(edit);

        var remove = new Button { Text = Loc.GetString("laws-ui-remove"), VerticalAlignment = Control.VAlignment.Top };
        remove.OnPressed += _ =>
        {
            ResetEditor();
            Send(LawsUiAction.Remove, index, string.Empty);
        };
        row.AddChild(remove);

        return row;
    }

    private void Submit()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0)
            return;

        if (_editIndex >= 0)
            Send(LawsUiAction.Edit, _editIndex, text);
        else
            Send(LawsUiAction.Add, -1, text);

        ResetEditor();
    }

    private void ResetEditor()
    {
        _editIndex = -1;
        _input.Text = string.Empty;
        _submit.Text = Loc.GetString("laws-ui-add");
        _cancel.Visible = false;
    }

    private void Send(LawsUiAction action, int index, string text)
    {
        _ui.SendMessage(new CartridgeUiMessage(new LawsUiMessageEvent(action, index, text)));
    }
}
