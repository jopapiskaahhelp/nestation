// Nestation New City CC BY-NC 3.0

using Content.Client.Gameplay;
using Content.Shared._Nestation.Tutorial;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._Nestation.Tutorial;

[UsedImplicitly]
public sealed partial class TutorialUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private TutorialWindow? _window;

    public void OnStateEntered(GameplayState state)
    {
        if (_cfg.GetCVar(TutorialCVars.Seen))
            return;

        _cfg.SetCVar(TutorialCVars.Seen, true);
        _cfg.SaveToFile();
        Open(true);
    }

    public void OnStateExited(GameplayState state)
    {
        _window?.Dispose();
        _window = null;
    }

    public void Open(bool askFirst)
    {
        _window?.Dispose();
        _window = UIManager.CreateWindow<TutorialWindow>();
        _window.Start(askFirst);
    }
}
