using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Contracts;

public interface IContactManagementStore
{
    Task<ContactManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ContactManagementSnapshot snapshot, CancellationToken cancellationToken = default);
}
