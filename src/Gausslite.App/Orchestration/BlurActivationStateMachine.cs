// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.App.Orchestration;

internal enum BlurActivationState
{
    Idle,
    Armed,
    Active
}

internal enum BlurActivationAction
{
    None
}

internal sealed class BlurActivationStateMachine
{
    public BlurActivationState State { get; private set; } = BlurActivationState.Idle;

    public BlurActivationAction Enable()
    {
        if (State != BlurActivationState.Idle)
            return BlurActivationAction.None;

        State = BlurActivationState.Armed;
        return BlurActivationAction.None;
    }

    public BlurActivationAction Disable()
    {
        State = BlurActivationState.Idle;
        return BlurActivationAction.None;
    }

    public BlurActivationAction Arm()
    {
        if (State != BlurActivationState.Idle)
            State = BlurActivationState.Armed;

        return BlurActivationAction.None;
    }

    public BlurActivationAction Activate()
    {
        if (State != BlurActivationState.Idle)
            State = BlurActivationState.Active;

        return BlurActivationAction.None;
    }
}
