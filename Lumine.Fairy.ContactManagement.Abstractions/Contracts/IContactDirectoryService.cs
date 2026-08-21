using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Contracts;

public interface IContactDirectoryService
{
    Task<IReadOnlyList<ContactProfile>> SearchContactsAsync(string keyword, CancellationToken cancellationToken = default);

    Task<ContactProfile?> GetContactByIdAsync(string contactId, CancellationToken cancellationToken = default);

    Task<ContactProfile?> MatchByAliasAsync(string alias, CancellationToken cancellationToken = default);
}
