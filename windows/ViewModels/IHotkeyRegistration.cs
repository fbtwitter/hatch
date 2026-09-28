namespace Hatch.ViewModels;

public interface IHotkeyRegistration
{
    bool IsRegistered { get; }
    bool ReRegister(uint modifiers, uint virtualKey);
}
