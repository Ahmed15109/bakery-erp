using System;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

public sealed class SafeContext : ISafeContext, IInternalSafeContext
{
    private SafeDto? _currentSafe;

    public int? CurrentSafeId => _currentSafe?.Id;
    public SafeDto? CurrentSafe => _currentSafe;

    public event EventHandler<SafeChangedEventArgs>? SafeChanged;

    public void ConfigureSafe(SafeDto safe)
    {
        if (_currentSafe?.Id != safe.Id)
        {
            _currentSafe = safe;
            SafeChanged?.Invoke(this, new SafeChangedEventArgs(safe));
        }
    }

    public void Clear()
    {
        if (_currentSafe != null)
        {
            _currentSafe = null;
            SafeChanged?.Invoke(this, new SafeChangedEventArgs(null));
        }
    }
}
