using CommunityToolkit.Mvvm.ComponentModel;

namespace TraceTiger.ViewModels;

/// <summary>One row of the parameter strip: a <c>&lt;TOKEN&gt;</c> found in the script and the
/// value that will replace it on Run.</summary>
public partial class ParameterEntryViewModel : ObservableObject
{
    public ParameterEntryViewModel(string token, string? initialValue)
    {
        Token = token;
        _value = initialValue ?? "";
    }

    public string Token { get; }

    [ObservableProperty]
    private string _value;
}
