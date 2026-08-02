using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bakery.WPF.ViewModels;

public sealed partial class PartyFormViewModel : ObservableObject
{
    private readonly int? _id;
    private readonly IValidationService? _validationService;
    private CancellationTokenSource? _validationCts;

    public PartyFormViewModel(IValidationService? validationService = null)
    {
        _validationService = validationService;
        PartyTypes = new[] { PartyType.Customer, PartyType.Supplier, PartyType.Mixed };
        IsActive = true;
    }

    public PartyFormViewModel(PartyDto party, IValidationService? validationService = null) : this(validationService)
    {
        _id = party.Id;
        Name = party.Name;
        SelectedType = party.Type;
        Phone = party.Phone;
        Address = party.Address;
        NationalId = party.NationalId;
        Notes = party.Notes;
        IsActive = party.IsActive;
        _ = ValidateNameAsync(Name);
    }

    public IReadOnlyList<PartyType> PartyTypes { get; }

    [ObservableProperty] private string name = "";
    [ObservableProperty] private PartyType selectedType = PartyType.Customer;
    [ObservableProperty] private string? phone;
    [ObservableProperty] private string? address;
    [ObservableProperty] private string? nationalId;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private bool isActive;

    [ObservableProperty] private bool? isNameValid;
    [ObservableProperty] private string nameValidationMessage = string.Empty;

    partial void OnNameChanged(string value)
    {
        _ = ValidateNameAsync(value);
    }

    private async Task ValidateNameAsync(string value)
    {
        if (_validationService == null) return;

        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;

        try
        {
            await Task.Delay(300, token);
            if (string.IsNullOrWhiteSpace(value))
            {
                IsNameValid = false;
                NameValidationMessage = "اسم الطرف مطلوب";
            }
            else
            {
                var used = await _validationService.IsPartyNameUsedAsync(value, _id);
                IsNameValid = !used;
                NameValidationMessage = used ? "الاسم مستخدم بالفعل" : "✅ متاح";
            }
        }
        catch (OperationCanceledException) { }
    }

    public SavePartyRequest ToRequest() => new(_id, Name, SelectedType, Phone, Address, NationalId, Notes, IsActive);
}
