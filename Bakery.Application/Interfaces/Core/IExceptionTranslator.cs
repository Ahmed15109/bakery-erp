namespace Bakery.Application.Interfaces;

public interface IExceptionTranslator
{
    string Translate(Exception ex);
    bool IsConcurrencyError(Exception ex);
}
