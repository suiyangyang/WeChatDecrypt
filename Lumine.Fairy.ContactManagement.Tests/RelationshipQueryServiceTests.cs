using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class RelationshipQueryServiceTests
{
    [Fact]
    public async Task GetPrimaryCompanyForContactAsync_ReturnsCompanyAndDepartment()
    {
        var store = new InMemoryStore(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "李四" }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "北极星软件" }
            ],
            RelationshipLinks =
            [
                new RelationshipLink
                {
                    SourceEntityId = "contact-1",
                    SourceEntityKind = EntityKind.Contact,
                    TargetEntityId = "company-1",
                    TargetEntityKind = EntityKind.Company,
                    LinkType = RelationshipLinkType.WorksAt,
                    DepartmentName = "交付部",
                    IsPrimary = true
                }
            ]
        });

        var service = new RelationshipQueryService(store);
        var result = await service.GetPrimaryCompanyForContactAsync("contact-1");

        Assert.NotNull(result.Company);
        Assert.Equal("北极星软件", result.Company!.Name);
        Assert.Equal("交付部", result.DepartmentName);
    }

    private sealed class InMemoryStore : IContactManagementStore
    {
        private readonly ContactManagementSnapshot _snapshot;

        public InMemoryStore(ContactManagementSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<ContactManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task SaveAsync(ContactManagementSnapshot snapshot, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
