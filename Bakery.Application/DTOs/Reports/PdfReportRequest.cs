using System;
using System.Collections.Generic;

namespace Bakery.Application.DTOs;

public record PdfReportRequest(
    string Title,
    IEnumerable<object> Data,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    List<(string Title, string Value, string? Suffix)>? SummaryCards = null
);
