// Nestation New City CC BY-NC 3.0

using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._Nestation.Tutorial;

public sealed class TutorialWindow : DefaultWindow
{
    private const int PageCount = 7;

    private readonly RichTextLabel _text = new() { VerticalExpand = true };

    private readonly BoxContainer _buttons = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        SeparationOverride = 6,
        HorizontalAlignment = HAlignment.Right
    };

    public TutorialWindow()
    {
        Title = Loc.GetString("tutorial-title");

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10,
            Margin = new Thickness(10),
            MinSize = new Vector2(460, 200)
        };

        root.AddChild(_text);
        root.AddChild(_buttons);
        ContentsContainer.AddChild(root);
    }

    public void Start(bool askFirst)
    {
        if (askFirst)
            ShowQuestion();
        else
            ShowPage(1);

        OpenCentered();
    }

    private void ShowQuestion()
    {
        SetText(Loc.GetString("tutorial-question"));
        _buttons.RemoveAllChildren();
        _buttons.AddChild(MakeButton(Loc.GetString("tutorial-yes"), () => ShowPage(1)));
        _buttons.AddChild(MakeButton(Loc.GetString("tutorial-no"), Close));
    }

    private void ShowPage(int page)
    {
        SetText(Loc.GetString($"tutorial-page-{page}"));
        _buttons.RemoveAllChildren();

        if (page >= PageCount)
            _buttons.AddChild(MakeButton(Loc.GetString("tutorial-thanks"), Close));
        else
            _buttons.AddChild(MakeButton(Loc.GetString("tutorial-next"), () => ShowPage(page + 1)));
    }

    private void SetText(string text)
    {
        _text.SetMessage(FormattedMessage.FromUnformatted(text));
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.OnPressed += _ => onPressed();
        return button;
    }
}
