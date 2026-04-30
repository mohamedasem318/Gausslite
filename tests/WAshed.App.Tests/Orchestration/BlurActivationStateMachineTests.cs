using WAshed.App.Orchestration;

namespace WAshed.App.Tests.Orchestration;

public sealed class BlurActivationStateMachineTests
{
    [Fact]
    public void Enable_WhenWindowUnavailable_TransitionsIdleToArmed()
    {
        var sut = new BlurActivationStateMachine();

        var action = sut.Enable();

        Assert.Equal(BlurActivationState.Armed, sut.State);
        Assert.Equal(BlurActivationAction.None, action);
    }

    [Fact]
    public void Activate_WhenArmed_TransitionsArmedToActive()
    {
        var sut = new BlurActivationStateMachine();
        sut.Enable();

        var action = sut.Activate();

        Assert.Equal(BlurActivationState.Active, sut.State);
        Assert.Equal(BlurActivationAction.None, action);
    }

    [Fact]
    public void Arm_WhenActive_TransitionsActiveToArmed()
    {
        var sut = new BlurActivationStateMachine();
        sut.Enable();
        sut.Activate();

        var action = sut.Arm();

        Assert.Equal(BlurActivationState.Armed, sut.State);
        Assert.Equal(BlurActivationAction.None, action);
    }

    [Fact]
    public void Arm_WhenIdle_LeavesStateIdle()
    {
        var sut = new BlurActivationStateMachine();

        var action = sut.Arm();

        Assert.Equal(BlurActivationState.Idle, sut.State);
        Assert.Equal(BlurActivationAction.None, action);
    }

    [Fact]
    public void Disable_FromArmed_ReturnsToIdleWithoutStoppingCapture()
    {
        var sut = new BlurActivationStateMachine();
        sut.Enable();

        var action = sut.Disable();

        Assert.Equal(BlurActivationState.Idle, sut.State);
        Assert.Equal(BlurActivationAction.None, action);
    }
}
