using System.Globalization;
using Avalonia.Data.Converters;
using Lumine.Fairy.ContactManagement.App.Helpers;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.Converters;

public sealed class EnumDisplayTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ContactRelationshipType relationshipType => ContactManagementDisplayText.GetContactRelationshipTypeText(relationshipType),
            EntityKind entityKind => ContactManagementDisplayText.GetEntityKindText(entityKind),
            RelationshipLinkType linkType => ContactManagementDisplayText.GetRelationshipLinkTypeText(linkType),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}