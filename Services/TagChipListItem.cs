using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuiverLauncher.Services;

public sealed class TagChipListItem : INotifyPropertyChanged
{
    private TagChipState _state;

    public string Tag { get; init; } = "";

    public TagChipState State
    {
        get => _state;
        set
        {
            if (_state == value)
                return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(IsInclude));
            OnPropertyChanged(nameof(IsExclude));
            OnPropertyChanged(nameof(IsNeutral));
        }
    }

    public bool IsNeutral => State == TagChipState.Neutral;
    public bool IsInclude => State == TagChipState.Include;
    public bool IsExclude => State == TagChipState.Exclude;

    public string StateLabel => State switch
    {
        TagChipState.Include => $"＋ {Tag}",
        TagChipState.Exclude => $"－ {Tag}",
        _ => Tag,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
