namespace WAshed.App.Orchestration;

public interface ITrayOrchestrator : IDisposable
{
    /// <summary>Fires after <see cref="IsBlurEnabled"/> changes. Argument is the new state.</summary>
    event EventHandler<bool>? BlurStateChanged;

    bool IsBlurEnabled { get; }

    void ToggleBlur();
    void EnableBlur();
    void DisableBlur();
}
