using System;
using Bakery.Application.DTOs.Accounting;

namespace Bakery.Application.Interfaces;

public interface ISafeContext
{
    int? CurrentSafeId { get; }
    SafeDto? CurrentSafe { get; }
    event EventHandler<SafeChangedEventArgs>? SafeChanged;
}

public class SafeChangedEventArgs : EventArgs
{
    public SafeDto? NewSafe { get; }

    public SafeChangedEventArgs(SafeDto? newSafe)
    {
        NewSafe = newSafe;
    }
}
