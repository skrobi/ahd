using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PzlEv.Shared.Utils.Ui.Mvvm;

/// <summary>Kolekcja z wymianą całej zawartości jednym powiadomieniem (tysiące wierszy tabeli bez tysięcy zdarzeń).</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
