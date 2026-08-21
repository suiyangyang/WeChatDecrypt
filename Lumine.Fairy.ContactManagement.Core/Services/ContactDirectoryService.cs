using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class ContactDirectoryService : IContactDirectoryService
{
    private readonly IContactManagementStore _store;

    public ContactDirectoryService(IContactManagementStore store)
    {
        _store = store;
    }

    public async Task<ContactProfile?> GetContactByIdAsync(string contactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
        {
            return null;
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        return snapshot.Contacts.FirstOrDefault(x => string.Equals(x.Id, contactId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ContactProfile?> MatchByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        var normalizedAlias = Normalize(alias);
        var snapshot = await _store.LoadAsync(cancellationToken);

        return snapshot.Contacts.FirstOrDefault(contact =>
            Normalize(contact.FullName) == normalizedAlias ||
            contact.Nicknames.Any(nickname => Normalize(nickname) == normalizedAlias));
    }

    public async Task<IReadOnlyList<ContactProfile>> SearchContactsAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return snapshot.Contacts.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var normalizedKeyword = Normalize(keyword);
        return snapshot.Contacts
            .Where(contact =>
                Normalize(contact.FullName).Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                contact.Nicknames.Any(nickname => Normalize(nickname).Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase)) ||
                contact.Emails.Any(email => email.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                contact.Phones.Any(phone => phone.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Normalize(string value) => value.Trim();
}
