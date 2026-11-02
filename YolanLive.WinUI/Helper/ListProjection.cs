using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YolanLive.WinUI.Helper;

internal static class ListProjection
{
    // Preserve item references and avoid Reset notifications while filtering or refreshing.
    internal static void Sync<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        var desired = source.ToArray();
        var retained = new HashSet<T>(desired);
        for (var i = target.Count - 1; i >= 0; i--) if (!retained.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++)
        {
            if (i < target.Count && EqualityComparer<T>.Default.Equals(target[i], desired[i])) continue;
            var previous = target.IndexOf(desired[i]);
            if (previous >= 0) target.Move(previous, i); else target.Insert(i, desired[i]);
        }
    }
    internal static bool Matches(string query, params string[] values) =>
        string.IsNullOrWhiteSpace(query) || values.Any(value => value?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true);
}
