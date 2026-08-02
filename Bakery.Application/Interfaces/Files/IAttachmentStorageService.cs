using System.Threading;
using System.Threading.Tasks;

namespace Bakery.Application.Interfaces;

public interface IAttachmentStorageService
{
    Task<string> SaveAttachmentAsync(string sourceFilePath, CancellationToken ct = default);
}
